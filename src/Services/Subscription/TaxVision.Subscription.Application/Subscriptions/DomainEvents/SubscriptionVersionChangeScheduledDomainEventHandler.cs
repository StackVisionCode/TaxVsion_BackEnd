using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions.Events;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.DomainEvents;

/// <summary>
/// D11: el aviso al cliente de un cambio de edición agendado. Viaja por el canal de estado con el motivo
/// VersionChangeScheduled (no es una transición: estado y estado previo son el mismo) para reusar la resolución del
/// destinatario en Auth y el envío en Notification. Cascada por el outbox.
/// </summary>
public static class SubscriptionVersionChangeScheduledDomainEventHandler
{
    public static TenantSubscriptionStatusChangedIntegrationEvent Handle(
        SubscriptionVersionChangeScheduledDomainEvent scheduled,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = scheduled.TenantId,
            TenantSubscriptionId = scheduled.TenantSubscriptionId,
            Status = scheduled.Status.ToString(),
            PreviousStatus = scheduled.Status.ToString(),
            Reason = nameof(SubscriptionChangeReason.VersionChangeScheduled),
            ActorUserId = scheduled.ActorUserId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            VersionChangeEffectiveAtUtc = scheduled.EffectiveAtUtc,
            VersionChangeSummary = scheduled.ChangeSummary,
            MigrationId = scheduled.MigrationId,
        };
}
