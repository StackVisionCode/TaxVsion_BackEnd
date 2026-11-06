using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.DomainEvents;

/// <summary>
/// El correo salió: devuelve el aviso a Notification (cascada por el outbox), o nada si no lo pidió Notification.
/// Corre después del commit, desde la cola domain-events. El envío ya quedó registrado: esto solo avisa.
/// </summary>
public static class SentMessageSentDomainEventHandler
{
    public static PostmasterEmailDeliverySucceededIntegrationEvent? Handle(SentMessageSentDomainEvent sent) =>
        sent.Callback is not { } callback
            ? null
            : new PostmasterEmailDeliverySucceededIntegrationEvent
            {
                TenantId = sent.TenantId,
                CorrelationId = callback.CorrelationId ?? string.Empty,
                NotificationLogId = callback.NotificationLogId,
                DispatchAttemptId = callback.DispatchAttemptId,
                SentMessageId = sent.SentMessageId,
                ProviderMessageId = sent.ProviderMessageId,
                CampaignId = callback.CampaignId,
                EventAtUtc = sent.OccurredAtUtc,
            };
}
