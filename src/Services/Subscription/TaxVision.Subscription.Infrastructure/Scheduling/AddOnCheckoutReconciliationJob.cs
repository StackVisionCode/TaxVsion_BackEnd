using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Reconcile-twin del checkout de add-ons: red de seguridad para cuando el <c>AddOnCheckoutPaidIntegrationEvent</c>
/// se pierde y la intención queda <c>Pending</c> pese a que el pago ya se confirmó — un cliente que pagó y no
/// recibió su add-on. Molde: <see cref="SeatCheckoutReconciliationJob"/>.
/// </summary>
public sealed class AddOnCheckoutReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<AddOnCheckoutReconciliationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(50))
{
    private const int BatchSize = 100;

    // Cadencia corta a propósito: una intención que el evento de PaymentApp ya resolvió deja de estar
    // Pending y no entra en el barrido, así que esto solo alcanza a las que se quedaron sin confirmar.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);

    protected override string JobName => "addon-checkout-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var intents = services.GetRequiredService<IAddOnPurchaseIntentRepository>();
        var jobLogger = services.GetRequiredService<ILogger<AddOnCheckoutReconciliationJob>>();

        var nowUtc = DateTime.UtcNow;
        var stale = await intents.FindStalePendingWithPaymentAsync(nowUtc - StaleAfter, BatchSize, ct);
        if (stale.Count == 0)
            return;

        var provisionedCount = await ReconcileAsync([.. stale.Select(intent => intent.Id)], nowUtc, ct);
        if (provisionedCount > 0)
            jobLogger.LogInformation(
                "AddOnCheckoutReconciliationJob activated {Count} paid-but-pending intent(s).",
                provisionedCount
            );
    }

    // Por intención: la consulta a PaymentApp va fuera de la transacción; después, una unidad (BB-3) en la que el
    // add-on, la intención, AddOnActivated y el recálculo se confirman juntos. Antes se publicaba antes de guardar.
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
                Logger.LogInformation("Add-on checkout intent {IntentId} changed by another writer.", intentId);
            else if (result.Outcome == JobUnitOutcome.Failed)
                Logger.LogWarning("Add-on checkout reconcile failed for intent {IntentId}.", intentId);
        }

        return provisionedCount;
    }

    private enum ReconcileOutcome
    {
        Nothing,
        Provisioned,
        MarkedFailed,
    }

    private async Task<AddOnPaymentStatusResult?> ReadPaymentStatusAsync(Guid intentId, CancellationToken ct)
    {
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var intent = await scope
                .ServiceProvider.GetRequiredService<IAddOnPurchaseIntentRepository>()
                .GetByIdForProvisioningAsync(intentId, ct);
            if (intent is not { Status: AddOnPurchaseIntentStatus.Pending, SaaSPaymentId: { } paymentId })
                return null;

            // null: PaymentApp no respondió — se reintenta el próximo tick.
            return await scope
                .ServiceProvider.GetRequiredService<IAddOnCheckoutPaymentClient>()
                .GetPaymentStatusAsync(intent.TenantId, paymentId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Add-on checkout reconcile failed for intent {IntentId}.", intentId);
            return null;
        }
    }

    private static async Task<ReconcileOutcome> ApplyAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid intentId,
        AddOnPaymentStatusResult status,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var intent = await unit
            .Services.GetRequiredService<IAddOnPurchaseIntentRepository>()
            .GetByIdForProvisioningAsync(intentId, ct);
        if (intent is null || intent.Status != AddOnPurchaseIntentStatus.Pending)
            return ReconcileOutcome.Nothing;

        unit.UseTenant(intent.TenantId);
        if (string.Equals(status.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            return await ProvisionAsync(unit, intent, status.PaidAtUtc ?? nowUtc, nowUtc, ct)
                ? ReconcileOutcome.Provisioned
                : ReconcileOutcome.Nothing;

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

    private static async Task<bool> ProvisionAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        AddOnPurchaseIntent intent,
        DateTime paidAtUtc,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var services = unit.Services;
        var logger = services.GetRequiredService<ILogger<AddOnCheckoutReconciliationJob>>();
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
            return false;
        }

        var definition = await services
            .GetRequiredService<IAddOnDefinitionRepository>()
            .GetByIdAsync(intent.AddOnDefinitionId, ct);
        if (definition is null)
        {
            logger.LogWarning("Reconcile: intent {IntentId} points to an unknown add-on.", intent.Id);
            return false;
        }

        var addOn = await AddOnCheckoutProvisioning.TryProvisionAsync(
            intent,
            subscription,
            definition,
            services.GetRequiredService<ITenantAddOnRepository>(),
            services.GetRequiredService<ISubscriptionAuditLogWriter>(),
            services.GetRequiredService<ISubscriptionMetrics>(),
            correlationId: intent.Id.ToString("N"),
            provisioningMethod: "CheckoutReconcile",
            paidAtUtc,
            nowUtc,
            ct
        );
        if (addOn is null)
            return false;

        await unit.PublishAsync(
            new AddOnActivatedIntegrationEvent
            {
                TenantId = intent.TenantId,
                TenantAddOnId = addOn.Id,
                AddOnCode = addOn.AddOnCode,
                Quantity = addOn.Quantity,
                CurrentPeriodEndUtc = addOn.CurrentPeriodEndUtc,
                CorrelationId = intent.Id.ToString("N"),
            }
        );
        await unit.PublishAsync(new RecalculateEntitlementsCommand(intent.TenantId));

        logger.LogInformation(
            "Reconcile activated add-on {AddOnCode} for tenant {TenantId} from paid-but-pending intent {IntentId}.",
            addOn.AddOnCode,
            intent.TenantId,
            intent.Id
        );
        return true;
    }
}
