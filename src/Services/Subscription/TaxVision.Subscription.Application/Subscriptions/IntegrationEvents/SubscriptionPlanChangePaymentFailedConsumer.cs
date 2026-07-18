using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Cierra el loop de upgrade en el caso de fallo: el plan nunca se tocó (ver
/// TenantSubscription.RequestUpgrade), así que acá solo se marca el request como
/// PaymentFailed — sin reintento automático, es un cargo interactivo iniciado por el
/// usuario, no dunning en background.</summary>
public static class SubscriptionPlanChangePaymentFailedConsumer
{
    public static async Task Handle(
        SubscriptionPlanChangePaymentFailedIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var subscription = await subscriptions.GetByTenantIdAsync(evt.TenantId, ct);
            if (subscription is null)
            {
                logger.LogWarning("SubscriptionPlanChangePaymentFailed for unknown tenant {TenantId}.", evt.TenantId);
                return;
            }

            var result = subscription.FailUpgradeCharge(evt.PlanChangeRequestId, evt.SaaSPaymentId, DateTime.UtcNow);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not record failed plan change charge for subscription {TenantSubscriptionId}: {Code}.", subscription.Id, result.Error.Code);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Plan change payment failed for subscription {TenantSubscriptionId}: {FailureCode} — {FailureReason}. Plan unchanged.",
                subscription.Id, evt.FailureCode, evt.FailureReason);
        }
    }
}
