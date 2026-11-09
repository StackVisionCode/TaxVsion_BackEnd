using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Reconcile-twin del checkout de renovación self-service: red de seguridad para cuando el
/// <c>SubscriptionRenewalCheckoutPaidIntegrationEvent</c> se pierde y la intención queda <c>Pending</c> pese a
/// que el pago ya se confirmó — un tenant que pagó su renovación y quedó sin reactivar. Cruza
/// <c>SubscriptionRenewalIntent.Pending</c> (con pago emitido) contra el estado real del <c>SaaSPayment</c>: si
/// <c>Succeeded</c>, reactiva (misma transición idempotente que el consumer, vía
/// <see cref="SubscriptionRenewalProvisioning"/>); si <c>Failed/Cancelled</c>, marca la intención fallida.
/// Molde: <c>SeatCheckoutReconciliationJob</c>.
/// </summary>
public sealed class SubscriptionRenewalCheckoutReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SubscriptionRenewalCheckoutReconciliationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(50))
{
    private const int BatchSize = 100;

    // Cadencia corta a propósito: una intención que el evento de PaymentApp ya resolvió deja de estar
    // Pending y no entra en el barrido, así que esto solo alcanza a las que se quedaron sin confirmar.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);

    protected override string JobName => "subscription-renewal-checkout-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var intents = services.GetRequiredService<IRenewalCheckoutIntentRepository>();
        var jobLogger = services.GetRequiredService<ILogger<SubscriptionRenewalCheckoutReconciliationJob>>();

        var nowUtc = DateTime.UtcNow;
        var stale = await intents.FindStalePendingWithPaymentAsync(nowUtc - StaleAfter, BatchSize, ct);
        if (stale.Count == 0)
            return;

        var reactivatedCount = await ReconcileAsync([.. stale.Select(intent => intent.Id)], nowUtc, ct);
        if (reactivatedCount > 0)
            jobLogger.LogInformation(
                "SubscriptionRenewalCheckoutReconciliationJob reactivated {Count} paid-but-pending intent(s).",
                reactivatedCount
            );
    }

    // Por intención: la consulta a PaymentApp va fuera de la transacción; después, una unidad (BB-3) en la que la
    // reactivación, su aviso SelfServiceRenewed (evento de dominio), los cobros superados y el recálculo se
    // confirman juntos. Un fallo o un conflicto no arrastra a las demás.
    internal async Task<int> ReconcileAsync(IReadOnlyList<Guid> intentIds, DateTime nowUtc, CancellationToken ct)
    {
        var reactivatedCount = 0;
        foreach (var intentId in intentIds)
        {
            var payment = await ReadPaymentStatusAsync(intentId, ct);
            if (payment is null)
                continue;

            var reactivated = false;
            var result = await RunUnitAsync(
                async (unit, token) =>
                {
                    var outcome = await ApplyAsync(unit, intentId, payment, nowUtc, token);
                    reactivated = outcome == ReconcileOutcome.Reactivated;
                    return outcome != ReconcileOutcome.Nothing;
                },
                ct
            );

            if (result.Outcome == JobUnitOutcome.Committed && reactivated)
                reactivatedCount++;
            else if (result.Outcome == JobUnitOutcome.Conflict)
                Logger.LogInformation("Renewal checkout intent {IntentId} changed by another writer.", intentId);
            else if (result.Outcome == JobUnitOutcome.Failed)
                Logger.LogWarning("Renewal checkout reconcile failed for intent {IntentId}.", intentId);
        }

        return reactivatedCount;
    }

    private enum ReconcileOutcome
    {
        Nothing,
        Reactivated,
        MarkedFailed,
    }

    private async Task<RenewalPaymentStatusResult?> ReadPaymentStatusAsync(Guid intentId, CancellationToken ct)
    {
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var intent = await scope
                .ServiceProvider.GetRequiredService<IRenewalCheckoutIntentRepository>()
                .GetByIdForProvisioningAsync(intentId, ct);
            if (intent is not { Status: SubscriptionRenewalIntentStatus.Pending, SaaSPaymentId: { } paymentId })
                return null;

            // null: PaymentApp no respondió — se reintenta el próximo tick.
            return await scope
                .ServiceProvider.GetRequiredService<IRenewalCheckoutPaymentClient>()
                .GetPaymentStatusAsync(intent.TenantId, paymentId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Renewal checkout reconcile failed for intent {IntentId}.", intentId);
            return null;
        }
    }

    private static async Task<ReconcileOutcome> ApplyAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid intentId,
        RenewalPaymentStatusResult status,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var logger = unit.Services.GetRequiredService<ILogger<SubscriptionRenewalCheckoutReconciliationJob>>();
        var intent = await unit
            .Services.GetRequiredService<IRenewalCheckoutIntentRepository>()
            .GetByIdForProvisioningAsync(intentId, ct);
        if (intent is null || intent.Status != SubscriptionRenewalIntentStatus.Pending)
            return ReconcileOutcome.Nothing;

        unit.UseTenant(intent.TenantId);
        if (string.Equals(status.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var subscription = await unit
                .Services.GetRequiredService<ISubscriptionRepository>()
                .GetByTenantIdAsync(intent.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning(
                    "Reconcile: tenant {TenantId} has no subscription for intent {IntentId}.",
                    intent.TenantId,
                    intent.Id
                );
                return ReconcileOutcome.Nothing;
            }

            var outcome = SubscriptionRenewalProvisioning.TryReactivate(
                intent,
                subscription,
                status.PaidAtUtc ?? nowUtc,
                nowUtc
            );
            if (!outcome.Provisioned)
                return ReconcileOutcome.Nothing;

            await unit.Outbox.PublishRenewalChargesSupersededAsync(
                intent.TenantId,
                outcome.SupersededChargeKeys,
                intent.Id.ToString("N")
            );
            await unit.PublishAsync(new RecalculateEntitlementsCommand(intent.TenantId));

            logger.LogInformation(
                "Reconcile reactivated subscription for tenant {TenantId} from paid-but-pending intent {IntentId} (was {PreviousStatus}).",
                intent.TenantId,
                intent.Id,
                outcome.PreviousStatus
            );
            return ReconcileOutcome.Reactivated;
        }

        if (
            string.Equals(status.Status, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        )
        {
            var failed = intent.MarkFailed($"Payment {status.Status} (reconciled).", nowUtc);
            return failed.IsSuccess ? ReconcileOutcome.MarkedFailed : ReconcileOutcome.Nothing;
        }

        return ReconcileOutcome.Nothing;
    }
}
