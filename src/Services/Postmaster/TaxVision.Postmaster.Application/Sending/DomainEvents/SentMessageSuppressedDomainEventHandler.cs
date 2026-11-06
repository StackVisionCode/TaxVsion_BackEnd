using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.DomainEvents;

/// <summary>Nadie iba a recibirlo: devuelve el aviso a Notification, o nada si no lo pidió Notification.</summary>
public static class SentMessageSuppressedDomainEventHandler
{
    public static PostmasterEmailDeliverySuppressedIntegrationEvent? Handle(
        SentMessageSuppressedDomainEvent suppressed
    ) =>
        suppressed.Callback is not { } callback
            ? null
            : new PostmasterEmailDeliverySuppressedIntegrationEvent
            {
                TenantId = suppressed.TenantId,
                CorrelationId = callback.CorrelationId ?? string.Empty,
                NotificationLogId = callback.NotificationLogId,
                DispatchAttemptId = callback.DispatchAttemptId,
                SentMessageId = suppressed.SentMessageId,
                SuppressionReason = suppressed.Reason,
                CampaignId = callback.CampaignId,
                EventAtUtc = suppressed.OccurredAtUtc,
            };
}
