using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions.Events;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.DomainEvents;

/// <summary>
/// La suscripción cambió de estado: devuelve el aviso de integración (cascada por el outbox). Corre después del
/// commit, desde la cola domain-events. Todo lo que lleva el aviso viene en el evento; no relee el agregado.
/// </summary>
public static class SubscriptionStatusChangedDomainEventHandler
{
    public static TenantSubscriptionStatusChangedIntegrationEvent Handle(
        SubscriptionStatusChangedDomainEvent changed,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = changed.TenantId,
            TenantSubscriptionId = changed.TenantSubscriptionId,
            Status = changed.Status.ToString(),
            PreviousStatus = changed.PreviousStatus.ToString(),
            Reason = changed.Reason.ToString(),
            AccessEndsAtUtc = changed.AccessEndsAtUtc,
            FailureCode = changed.FailureCode,
            ActorUserId = changed.ActorUserId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            NotifiedByChargeFailedEmail = changed.NotifiedByChargeFailedEmail,
        };
}
