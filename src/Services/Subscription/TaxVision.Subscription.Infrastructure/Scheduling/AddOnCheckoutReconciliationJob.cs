using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;

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
        var context = new ReconcileContext(
            services.GetRequiredService<ISubscriptionRepository>(),
            services.GetRequiredService<IAddOnDefinitionRepository>(),
            services.GetRequiredService<ITenantAddOnRepository>(),
            services.GetRequiredService<IAddOnCheckoutPaymentClient>(),
            services.GetRequiredService<ISubscriptionAuditLogWriter>(),
            services.GetRequiredService<ISubscriptionMetrics>(),
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IMessageBus>(),
            services.GetRequiredService<ILogger<AddOnCheckoutReconciliationJob>>()
        );

        var nowUtc = DateTime.UtcNow;
        var stale = await intents.FindStalePendingWithPaymentAsync(nowUtc - StaleAfter, BatchSize, ct);
        if (stale.Count == 0)
            return;

        var provisionedCount = 0;
        foreach (var intent in stale)
        {
            try
            {
                if (await ReconcileOneAsync(intent, context, nowUtc, ct))
                    provisionedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.Logger.LogError(ex, "Add-on checkout reconcile failed for intent {IntentId}.", intent.Id);
            }
        }

        if (provisionedCount > 0)
            context.Logger.LogInformation(
                "AddOnCheckoutReconciliationJob activated {Count} paid-but-pending intent(s).",
                provisionedCount
            );
    }

    private sealed record ReconcileContext(
        ISubscriptionRepository Subscriptions,
        IAddOnDefinitionRepository Definitions,
        ITenantAddOnRepository TenantAddOns,
        IAddOnCheckoutPaymentClient Payments,
        ISubscriptionAuditLogWriter Audit,
        ISubscriptionMetrics Metrics,
        IUnitOfWork UnitOfWork,
        IMessageBus Bus,
        ILogger Logger
    );

    private static async Task<bool> ReconcileOneAsync(
        AddOnPurchaseIntent intent,
        ReconcileContext context,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (intent.SaaSPaymentId is not { } paymentId)
            return false;

        var status = await context.Payments.GetPaymentStatusAsync(intent.TenantId, paymentId, ct);
        if (status is null)
            return false; // PaymentApp no respondió — se reintenta el próximo tick

        if (string.Equals(status.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            return await ProvisionAsync(intent, context, status.PaidAtUtc ?? nowUtc, nowUtc, ct);

        if (
            string.Equals(status.Status, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        )
        {
            var failed = intent.MarkFailed($"Payment {status.Status} (reconciled).", nowUtc);
            if (failed.IsSuccess)
                await context.UnitOfWork.SaveChangesAsync(ct);
        }

        return false;
    }

    private static async Task<bool> ProvisionAsync(
        AddOnPurchaseIntent intent,
        ReconcileContext context,
        DateTime paidAtUtc,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var subscription = await context.Subscriptions.GetByTenantIdAsync(intent.TenantId, ct);
        if (subscription is null)
        {
            context.Logger.LogWarning(
                "Reconcile: tenant {TenantId} has no subscription for intent {IntentId}.",
                intent.TenantId,
                intent.Id
            );
            return false;
        }

        var definition = await context.Definitions.GetByIdAsync(intent.AddOnDefinitionId, ct);
        if (definition is null)
        {
            context.Logger.LogWarning("Reconcile: intent {IntentId} points to an unknown add-on.", intent.Id);
            return false;
        }

        var addOn = await AddOnCheckoutProvisioning.TryProvisionAsync(
            intent,
            subscription,
            definition,
            context.TenantAddOns,
            context.Audit,
            context.Metrics,
            correlationId: intent.Id.ToString("N"),
            provisioningMethod: "CheckoutReconcile",
            paidAtUtc,
            nowUtc,
            ct
        );
        if (addOn is null)
            return false;

        await context.Bus.PublishAsync(
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
        await context.UnitOfWork.SaveChangesAsync(ct);

        context.Bus.TenantId = intent.TenantId.ToString();
        await context.Bus.RecalculateEntitlementsSafelyAsync(intent.TenantId, context.Logger, ct);

        context.Logger.LogInformation(
            "Reconcile activated add-on {AddOnCode} for tenant {TenantId} from paid-but-pending intent {IntentId}.",
            addOn.AddOnCode,
            intent.TenantId,
            intent.Id
        );
        return true;
    }
}
