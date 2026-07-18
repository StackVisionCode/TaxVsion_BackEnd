using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Cierra el loop de renovación: PaymentApp confirmó el cobro, así que la
/// suscripción base avanza su período. Sin este consumer, <see cref="TenantSubscription.BeginRenewal"/>
/// dejaba el renewal en Scheduled para siempre.</summary>
public static class SubscriptionRenewalPaymentSucceededConsumer
{
    public static async Task Handle(
        SubscriptionRenewalPaymentSucceededIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var subscription = await subscriptions.GetByTenantIdAsync(evt.TenantId, ct);
            if (subscription is null || subscription.Id != evt.TenantSubscriptionId)
            {
                logger.LogWarning(
                    "SubscriptionRenewalPaymentSucceeded for unknown subscription {TenantSubscriptionId}.",
                    evt.TenantSubscriptionId
                );
                return;
            }

            var renewal = FindRenewalByKey(subscription, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning(
                    "SubscriptionRenewalPaymentSucceeded for {TenantSubscriptionId} has no matching renewal for key {Key}.",
                    evt.TenantSubscriptionId,
                    evt.IdempotencyKey
                );
                return;
            }

            var result = subscription.CompleteRenewal(
                renewal.Id,
                evt.ExternalPaymentReference,
                actorUserId: Guid.Empty,
                evt.PaidAtUtc
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not complete renewal for subscription {TenantSubscriptionId}: {Code}.",
                    subscription.Id,
                    result.Error.Code
                );
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
