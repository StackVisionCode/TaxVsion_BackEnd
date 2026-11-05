using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
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

    // Cada intención en su propio scope: un fallo deja sucio solo su DbContext.
    internal async Task<int> ReconcileAsync(IReadOnlyList<Guid> intentIds, DateTime nowUtc, CancellationToken ct)
    {
        var reactivatedCount = 0;
        foreach (var intentId in intentIds)
        {
            using var itemScope = ScopeFactory.CreateScope();
            var services = itemScope.ServiceProvider;
            var jobLogger = services.GetRequiredService<ILogger<SubscriptionRenewalCheckoutReconciliationJob>>();
            try
            {
                var intent = await services
                    .GetRequiredService<IRenewalCheckoutIntentRepository>()
                    .GetByIdForProvisioningAsync(intentId, ct);
                if (
                    intent is { Status: SubscriptionRenewalIntentStatus.Pending }
                    && await ReconcileOneAsync(
                        intent,
                        services.GetRequiredService<ISubscriptionRepository>(),
                        services.GetRequiredService<IRenewalCheckoutPaymentClient>(),
                        services.GetRequiredService<IUnitOfWork>(),
                        services.GetRequiredService<IMessageBus>(),
                        jobLogger,
                        nowUtc,
                        ct
                    )
                )
                    reactivatedCount++;
            }
            catch (DbUpdateConcurrencyException)
            {
                jobLogger.LogInformation("Renewal checkout intent {IntentId} changed by another writer.", intentId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                jobLogger.LogError(ex, "Renewal checkout reconcile failed for intent {IntentId}.", intentId);
            }
        }

        return reactivatedCount;
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

            await bus.PublishRenewalChargesSupersededAsync(
                intent.TenantId,
                outcome.SupersededChargeKeys,
                intent.Id.ToString("N")
            );
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
