using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Seats;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Reconcile-twin del checkout de asientos: red de seguridad para cuando el <c>SeatsCheckoutPaidIntegrationEvent</c>
/// de PaymentApp se pierde (Subscription caído al publicarse, error transitorio del consumer, etc.) y la
/// intención queda <c>Pending</c> pese a que el pago ya se confirmó — un cliente que pagó y no recibió su asiento.
/// Cruza <c>SeatPurchaseIntent.Pending</c> (con pago ya emitido) contra el estado real del <c>SaaSPayment</c> en
/// PaymentApp: si <c>Succeeded</c>, aprovisiona (misma transición idempotente que el consumer, vía
/// <see cref="SeatCheckoutProvisioning"/>); si <c>Failed/Cancelled</c>, marca la intención fallida. Sólo mira
/// intenciones con cierta antigüedad para no competir con el flujo normal del webhook.
/// </summary>
public sealed class SeatCheckoutReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SeatCheckoutReconciliationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(50))
{
    private const int BatchSize = 100;

    // Cadencia corta a propósito: una intención que el evento de PaymentApp ya resolvió deja de estar
    // Pending y no entra en el barrido, así que esto solo alcanza a las que se quedaron sin confirmar.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);

    protected override string JobName => "seat-checkout-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var intents = services.GetRequiredService<ISeatPurchaseIntentRepository>();
        var jobLogger = services.GetRequiredService<ILogger<SeatCheckoutReconciliationJob>>();

        var nowUtc = DateTime.UtcNow;
        var stale = await intents.FindStalePendingWithPaymentAsync(nowUtc - StaleAfter, BatchSize, ct);
        if (stale.Count == 0)
            return;

        var provisionedCount = await ReconcileAsync([.. stale.Select(intent => intent.Id)], nowUtc, ct);
        if (provisionedCount > 0)
            jobLogger.LogInformation(
                "SeatCheckoutReconciliationJob provisioned {Count} paid-but-pending intent(s).",
                provisionedCount
            );
    }

    // Por intención: la consulta a PaymentApp va fuera de la transacción; después, una unidad (BB-3) en la que los
    // asientos, la intención y el recálculo se confirman juntos. Un fallo o un conflicto no arrastra a las demás.
    internal async Task<int> ReconcileAsync(IReadOnlyList<Guid> intentIds, DateTime nowUtc, CancellationToken ct)
    {
        var provisionedCount = 0;
        foreach (var intentId in intentIds)
        {
            var payment = await ReadPaymentStatusAsync(intentId, ct);
            if (payment is null)
                continue;

            var provisioned = false;
            var result = await RunUnitAsync(
                async (unit, token) =>
                {
                    var outcome = await ApplyAsync(unit, intentId, payment, nowUtc, token);
                    provisioned = outcome == ReconcileOutcome.Provisioned;
                    return outcome != ReconcileOutcome.Nothing;
                },
                ct
            );

            if (result.Outcome == JobUnitOutcome.Committed && provisioned)
                provisionedCount++;
            else if (result.Outcome == JobUnitOutcome.Conflict)
                Logger.LogInformation("Seat checkout intent {IntentId} provisioned by another writer.", intentId);
            else if (result.Outcome == JobUnitOutcome.Failed)
                Logger.LogWarning("Seat checkout reconcile failed for intent {IntentId}.", intentId);
        }

        return provisionedCount;
    }

    private enum ReconcileOutcome
    {
        Nothing,
        Provisioned,
        MarkedFailed,
    }

    private async Task<SeatPaymentStatusResult?> ReadPaymentStatusAsync(Guid intentId, CancellationToken ct)
    {
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var intent = await scope
                .ServiceProvider.GetRequiredService<ISeatPurchaseIntentRepository>()
                .GetByIdForProvisioningAsync(intentId, ct);
            if (intent is not { Status: SeatPurchaseIntentStatus.Pending, SaaSPaymentId: { } paymentId })
                return null;

            // null: PaymentApp no respondió — se reintenta el próximo tick.
            return await scope
                .ServiceProvider.GetRequiredService<ISeatCheckoutPaymentClient>()
                .GetPaymentStatusAsync(intent.TenantId, paymentId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Seat checkout reconcile failed for intent {IntentId}.", intentId);
            return null;
        }
    }

    private static async Task<ReconcileOutcome> ApplyAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid intentId,
        SeatPaymentStatusResult status,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var services = unit.Services;
        var logger = services.GetRequiredService<ILogger<SeatCheckoutReconciliationJob>>();
        var intent = await services
            .GetRequiredService<ISeatPurchaseIntentRepository>()
            .GetByIdForProvisioningAsync(intentId, ct);
        if (intent is null || intent.Status != SeatPurchaseIntentStatus.Pending)
            return ReconcileOutcome.Nothing;

        unit.UseTenant(intent.TenantId);
        if (string.Equals(status.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var subscription = await services
                .GetRequiredService<ISubscriptionRepository>()
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

            var provisioned = await SeatCheckoutProvisioning.TryProvisionAsync(
                intent,
                subscription,
                services.GetRequiredService<ISubscriptionSeatRepository>(),
                services.GetRequiredService<ISubscriptionAuditLogWriter>(),
                services.GetRequiredService<ISubscriptionMetrics>(),
                correlationId: intent.Id.ToString("N"),
                provisioningMethod: "CheckoutReconcile",
                paidAtUtc: status.PaidAtUtc ?? nowUtc,
                nowUtc,
                ct
            );
            if (!provisioned)
                return ReconcileOutcome.Nothing;

            await unit.PublishAsync(new RecalculateEntitlementsCommand(intent.TenantId));
            logger.LogInformation(
                "Reconcile provisioned {Quantity} {SeatType} seat(s) for tenant {TenantId} from paid-but-pending intent {IntentId}.",
                intent.Quantity,
                intent.SeatType,
                intent.TenantId,
                intent.Id
            );
            return ReconcileOutcome.Provisioned;
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
