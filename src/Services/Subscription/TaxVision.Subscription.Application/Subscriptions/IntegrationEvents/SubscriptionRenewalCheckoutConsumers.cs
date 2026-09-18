using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Cierra el flujo de renovación/reactivación self-service por HOSTED-CHECKOUT (Expiración/Dunning, Fase 4): al
/// confirmarse el pago, REACTIVA la suscripción (PastDue/GracePeriod/Suspended/Expired → Active con período
/// nuevo) SIN volver a cobrar — el checkout ya cobró — y publica el <c>TenantSubscriptionStatusChanged</c> con
/// motivo <see cref="SubscriptionChangeReason.SelfServiceRenewed"/> (Auth desbloquea acceso y manda el email de
/// reactivación). Idempotente por el estado de la intención. Molde: <c>SeatsCheckoutPaidConsumer</c>.
/// </summary>
public static class SubscriptionRenewalCheckoutPaidConsumer
{
    public static async Task Handle(
        SubscriptionRenewalCheckoutPaidIntegrationEvent evt,
        IRenewalCheckoutIntentRepository intents,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionMetrics metrics,
        ILogger<SubscriptionRenewalIntent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var intent = await intents.GetByIdForProvisioningAsync(evt.RenewalIntentId, ct);
            if (intent is null)
            {
                logger.LogWarning("RenewalCheckoutPaid for unknown intent {IntentId}.", evt.RenewalIntentId);
                return;
            }

            if (intent.TenantId != evt.TenantId)
            {
                logger.LogWarning("RenewalCheckoutPaid tenant mismatch for intent {IntentId}.", evt.RenewalIntentId);
                return;
            }

            if (intent.Status == SubscriptionRenewalIntentStatus.Provisioned)
                return; // ya reactivada (redelivery)

            var subscription = await subscriptions.GetByTenantIdAsync(evt.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning("RenewalCheckoutPaid for tenant {TenantId} without a subscription.", evt.TenantId);
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var outcome = SubscriptionRenewalProvisioning.TryReactivate(intent, subscription, evt.PaidAtUtc, nowUtc);
            if (!outcome.Provisioned)
            {
                logger.LogWarning(
                    "Cannot reactivate from renewal intent {IntentId} (status {Status}).",
                    intent.Id,
                    intent.Status
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            bus.TenantId = evt.TenantId.ToString();
            if (outcome.StatusChanged)
            {
                await bus.PublishStatusChangedAsync(
                    subscription,
                    outcome.PreviousStatus,
                    SubscriptionChangeReason.SelfServiceRenewed,
                    intent.RequestedByUserId,
                    correlationId: correlationId
                );
            }

            await bus.RecalculateEntitlementsSafelyAsync(evt.TenantId, logger, ct);
            metrics.RecordSelfServiceRenewal("succeeded");

            logger.LogInformation(
                "Reactivated subscription for tenant {TenantId} from paid renewal checkout intent {IntentId} (was {PreviousStatus}).",
                evt.TenantId,
                intent.Id,
                outcome.PreviousStatus
            );
        }
    }
}

/// <summary>Marca fallida la intención de renovación cuando el checkout falla/expira. NO cambia el estado de la
/// suscripción (sigue en su lapso). Molde: <c>SeatsCheckoutFailedConsumer</c>.</summary>
public static class SubscriptionRenewalCheckoutFailedConsumer
{
    public static async Task Handle(
        SubscriptionRenewalCheckoutFailedIntegrationEvent evt,
        IRenewalCheckoutIntentRepository intents,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ISubscriptionMetrics metrics,
        ILogger<SubscriptionRenewalIntent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var intent = await intents.GetByIdForProvisioningAsync(evt.RenewalIntentId, ct);
            if (intent is null)
            {
                logger.LogWarning("RenewalCheckoutFailed for unknown intent {IntentId}.", evt.RenewalIntentId);
                return;
            }

            if (intent.TenantId != evt.TenantId)
            {
                logger.LogWarning("RenewalCheckoutFailed tenant mismatch for intent {IntentId}.", evt.RenewalIntentId);
                return;
            }

            var failed = intent.MarkFailed($"{evt.FailureCode}: {evt.FailureReason}", DateTime.UtcNow);
            if (failed.IsFailure)
            {
                logger.LogWarning(
                    "Cannot mark renewal intent {IntentId} failed ({Error}).",
                    intent.Id,
                    failed.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            metrics.RecordSelfServiceRenewal("failed");
        }
    }
}
