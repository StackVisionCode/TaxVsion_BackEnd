using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Cierra el loop de renovación en el caso de fallo: si PaymentApp ya agotó sus
/// reintentos (<c>WillRetry = false</c>), la suscripción pasa a PastDue y el resto del
/// pipeline existente (grace period, suspensión) sigue su curso normal — este consumer no
/// reimplementa esa lógica, solo la dispara.</summary>
public static class SubscriptionRenewalPaymentFailedConsumer
{
    public static async Task Handle(
        SubscriptionRenewalPaymentFailedIntegrationEvent evt,
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
            if (subscription is null || subscription.Id != evt.TenantSubscriptionId)
            {
                logger.LogWarning("SubscriptionRenewalPaymentFailed for unknown subscription {TenantSubscriptionId}.", evt.TenantSubscriptionId);
                return;
            }

            var renewal = FindRenewalByKey(subscription, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning(
                    "SubscriptionRenewalPaymentFailed for {TenantSubscriptionId} has no matching renewal for key {Key}.",
                    evt.TenantSubscriptionId, evt.IdempotencyKey);
                return;
            }

            var result = subscription.FailRenewal(
                renewal.Id, evt.FailureCode, evt.FailureReason, evt.WillRetry, evt.NextRetryAtUtc, actorUserId: Guid.Empty, DateTime.UtcNow);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not record failed renewal for subscription {TenantSubscriptionId}: {Code}.", subscription.Id, result.Error.Code);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static TenantSubscriptionRenewal? FindRenewalByKey(TenantSubscription subscription, string idempotencyKey)
    {
        foreach (var renewal in subscription.Renewals)
        {
            if (renewal.IdempotencyKey == idempotencyKey)
                return renewal;
        }

        return null;
    }
}
