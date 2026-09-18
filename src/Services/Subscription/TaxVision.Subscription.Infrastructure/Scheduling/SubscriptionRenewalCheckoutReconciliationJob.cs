using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

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
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2))
{
    private const int BatchSize = 100;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(3);

    protected override string JobName => "subscription-renewal-checkout-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var intents = services.GetRequiredService<IRenewalCheckoutIntentRepository>();
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var payments = services.GetRequiredService<IRenewalCheckoutPaymentClient>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var bus = services.GetRequiredService<IMessageBus>();
        var jobLogger = services.GetRequiredService<ILogger<SubscriptionRenewalCheckoutReconciliationJob>>();

        var nowUtc = DateTime.UtcNow;
        var stale = await intents.FindStalePendingWithPaymentAsync(nowUtc - StaleAfter, BatchSize, ct);
        if (stale.Count == 0)
            return;

        var reactivatedCount = 0;
        foreach (var intent in stale)
        {
            try
            {
                if (await ReconcileOneAsync(intent, subscriptions, payments, unitOfWork, bus, jobLogger, nowUtc, ct))
                    reactivatedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                jobLogger.LogError(ex, "Renewal checkout reconcile failed for intent {IntentId}.", intent.Id);
            }
        }

        if (reactivatedCount > 0)
            jobLogger.LogInformation(
                "SubscriptionRenewalCheckoutReconciliationJob reactivated {Count} paid-but-pending intent(s).",
                reactivatedCount
            );
    }

    private static async Task<bool> ReconcileOneAsync(
        SubscriptionRenewalIntent intent,
        ISubscriptionRepository subscriptions,
        IRenewalCheckoutPaymentClient payments,
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

            var outcome = SubscriptionRenewalProvisioning.TryReactivate(
                intent,
                subscription,
                status.PaidAtUtc ?? nowUtc,
                nowUtc
            );
            if (!outcome.Provisioned)
                return false;

            await unitOfWork.SaveChangesAsync(ct);
            bus.TenantId = intent.TenantId.ToString();
            if (outcome.StatusChanged)
            {
                await bus.PublishStatusChangedAsync(
                    subscription,
                    outcome.PreviousStatus,
                    SubscriptionChangeReason.SelfServiceRenewed,
                    intent.RequestedByUserId,
                    correlationId: intent.Id.ToString("N")
                );
            }

            await bus.RecalculateEntitlementsSafelyAsync(intent.TenantId, logger, ct);

            logger.LogInformation(
                "Reconcile reactivated subscription for tenant {TenantId} from paid-but-pending intent {IntentId} (was {PreviousStatus}).",
                intent.TenantId,
                intent.Id,
                outcome.PreviousStatus
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
