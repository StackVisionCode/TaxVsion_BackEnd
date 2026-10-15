using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Cierra el loop de renovación en el caso de fallo: si PaymentApp ya agotó sus reintentos
/// (<c>WillRetry = false</c>), la suscripción pasa a PastDue y de inmediato a Suspended (sin ventana
/// de gracia: el usuario pierde acceso al vencer). <c>SubscriptionExpirationJob</c> expira después.</summary>
public static class SubscriptionRenewalPaymentFailedConsumer
{
    public static async Task Handle(
        SubscriptionRenewalPaymentFailedIntegrationEvent evt,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
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
                    "SubscriptionRenewalPaymentFailed for unknown subscription {TenantSubscriptionId}.",
                    evt.TenantSubscriptionId
                );
                return;
            }

            var renewal = FindRenewalByKey(subscription, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning(
                    "SubscriptionRenewalPaymentFailed for {TenantSubscriptionId} has no matching renewal for key {Key}.",
                    evt.TenantSubscriptionId,
                    evt.IdempotencyKey
                );
                return;
            }

            var previousStatus = subscription.Status;
            var result = subscription.FailRenewal(
                renewal.Id,
                evt.FailureCode,
                evt.FailureReason,
                evt.WillRetry,
                evt.NextRetryAtUtc,
                actorUserId: Guid.Empty,
                DateTime.UtcNow
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not record failed renewal for subscription {TenantSubscriptionId}: {Code}.",
                    subscription.Id,
                    result.Error.Code
                );
                return;
            }

            // Al agotarse los reintentos (Active→PastDue) se suspende de una: ya no se da ventana con acceso.
            // Un fallo con reintento pendiente no transiciona.
            if (subscription.Status == SubscriptionStatus.PastDue && previousStatus != SubscriptionStatus.PastDue)
            {
                var suspendResult = subscription.SuspendAfterRetriesExhausted(Guid.Empty, DateTime.UtcNow);
                if (suspendResult.IsFailure)
                    logger.LogWarning(
                        "Could not suspend subscription {TenantSubscriptionId} after retries exhausted: {Code}.",
                        subscription.Id,
                        suspendResult.Error.Code
                    );
            }

            await unitOfWork.SaveChangesAsync(ct);

            // Solo cuando el fallo efectivamente movió el estado (Active→PastDue→Suspended) — un fallo con
            // reintento pendiente no transiciona y no debe emitir el evento de ciclo de vida.
            if (subscription.Status != previousStatus)
            {
                bus.TenantId = evt.TenantId.ToString();
                await bus.PublishStatusChangedAsync(
                    subscription,
                    previousStatus,
                    SubscriptionChangeReason.RenewalPaymentFailed,
                    failureCode: evt.FailureCode,
                    correlationId: correlationId
                );
            }
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
