using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Cierra el loop de renovación: PaymentApp confirmó el cobro, así que la suscripción base avanza
/// su período. Fase 1 — si el pago se recuperó mientras la sub estaba PastDue/GracePeriod, primero la
/// devuelve a Active (recuperación) para que <see cref="TenantSubscription.CompleteRenewal"/>, que exige
/// Active, pueda aplicar el renewal. Sin este consumer, <see cref="TenantSubscription.BeginRenewal"/>
/// dejaba el renewal en Scheduled para siempre.</summary>
public static class SubscriptionRenewalPaymentSucceededConsumer
{
    public static async Task Handle(
        SubscriptionRenewalPaymentSucceededIntegrationEvent evt,
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

            // Fase 1 — recuperación: un cobro exitoso mientras la sub está PastDue/GracePeriod la devuelve a
            // Active ANTES de aplicar el renewal (CompleteRenewal exige Active). Idempotente: si ya está
            // Active, no hace nada.
            var previousStatus = subscription.Status;
            var recovery = subscription.Status switch
            {
                SubscriptionStatus.PastDue => subscription.RecoverFromPastDue(Guid.Empty, evt.PaidAtUtc),
                SubscriptionStatus.GracePeriod => subscription.RecoverFromGracePeriod(Guid.Empty, evt.PaidAtUtc),
                _ => Result.Success(),
            };
            if (recovery.IsFailure)
            {
                logger.LogWarning(
                    "Could not recover subscription {TenantSubscriptionId} on renewal success: {Code}.",
                    subscription.Id,
                    recovery.Error.Code
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

            // Publica la recuperación (PastDue/GracePeriod→Active) para que dunning/UX se enteren.
            if (
                previousStatus is SubscriptionStatus.PastDue or SubscriptionStatus.GracePeriod
                && subscription.Status == SubscriptionStatus.Active
            )
            {
                bus.TenantId = evt.TenantId.ToString();
                await bus.PublishStatusChangedAsync(
                    subscription,
                    previousStatus,
                    SubscriptionChangeReason.PaymentRecovered,
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
