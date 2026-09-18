using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Seats;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

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
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2))
{
    private const int BatchSize = 100;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(3);

    protected override string JobName => "seat-checkout-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var intents = services.GetRequiredService<ISeatPurchaseIntentRepository>();
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var payments = services.GetRequiredService<ISeatCheckoutPaymentClient>();
        var audit = services.GetRequiredService<ISubscriptionAuditLogWriter>();
        var metrics = services.GetRequiredService<ISubscriptionMetrics>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var bus = services.GetRequiredService<IMessageBus>();
        var jobLogger = services.GetRequiredService<ILogger<SeatCheckoutReconciliationJob>>();

        var nowUtc = DateTime.UtcNow;
        var stale = await intents.FindStalePendingWithPaymentAsync(nowUtc - StaleAfter, BatchSize, ct);
        if (stale.Count == 0)
            return;

        var provisionedCount = 0;
        foreach (var intent in stale)
        {
            try
            {
                if (
                    await ReconcileOneAsync(
                        intent,
                        subscriptions,
                        seats,
                        payments,
                        audit,
                        metrics,
                        unitOfWork,
                        bus,
                        jobLogger,
                        nowUtc,
                        ct
                    )
                )
                    provisionedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                jobLogger.LogError(ex, "Seat checkout reconcile failed for intent {IntentId}.", intent.Id);
            }
        }

        if (provisionedCount > 0)
            jobLogger.LogInformation(
                "SeatCheckoutReconciliationJob provisioned {Count} paid-but-pending intent(s).",
                provisionedCount
            );
    }

    private static async Task<bool> ReconcileOneAsync(
        SeatPurchaseIntent intent,
        ISubscriptionRepository subscriptions,
        ISubscriptionSeatRepository seats,
        ISeatCheckoutPaymentClient payments,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (intent.SaaSPaymentId is not { } paymentId)
            return false;

        var status = await payments.GetPaymentStatusAsync(intent.TenantId, paymentId, ct);
        if (status is null)
            return false; // PaymentApp no respondió — se reintenta el próximo tick

        if (string.Equals(status.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var subscription = await subscriptions.GetByTenantIdAsync(intent.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning(
                    "Reconcile: tenant {TenantId} has no subscription for intent {IntentId}.",
                    intent.TenantId,
                    intent.Id
                );
                return false;
            }

            var provisioned = await SeatCheckoutProvisioning.TryProvisionAsync(
                intent,
                subscription,
                seats,
                audit,
                metrics,
                correlationId: intent.Id.ToString("N"),
                provisioningMethod: "CheckoutReconcile",
                paidAtUtc: status.PaidAtUtc ?? nowUtc,
                nowUtc,
                ct
            );
            if (!provisioned)
                return false;

            await unitOfWork.SaveChangesAsync(ct);
            bus.TenantId = intent.TenantId.ToString();
            await bus.RecalculateEntitlementsSafelyAsync(intent.TenantId, logger, ct);

            logger.LogInformation(
                "Reconcile provisioned {Quantity} {SeatType} seat(s) for tenant {TenantId} from paid-but-pending intent {IntentId}.",
                intent.Quantity,
                intent.SeatType,
                intent.TenantId,
                intent.Id
            );
            return true;
        }

        if (
            string.Equals(status.Status, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        )
        {
            var failed = intent.MarkFailed($"Payment {status.Status} (reconciled).", nowUtc);
            if (failed.IsSuccess)
                await unitOfWork.SaveChangesAsync(ct);
        }

        return false;
    }
}
