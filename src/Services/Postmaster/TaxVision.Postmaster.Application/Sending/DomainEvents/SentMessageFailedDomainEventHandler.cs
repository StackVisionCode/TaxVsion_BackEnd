using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.DomainEvents;

/// <summary>El correo no salió: devuelve el aviso a Notification, o nada si no lo pidió Notification.</summary>
public static class SentMessageFailedDomainEventHandler
{
    public static PostmasterEmailDeliveryFailedIntegrationEvent? Handle(SentMessageFailedDomainEvent failed) =>
        failed.Callback is not { } callback
            ? null
            : new PostmasterEmailDeliveryFailedIntegrationEvent
            {
                TenantId = failed.TenantId,
                CorrelationId = callback.CorrelationId ?? string.Empty,
                NotificationLogId = callback.NotificationLogId,
                DispatchAttemptId = callback.DispatchAttemptId,
                SentMessageId = failed.SentMessageId,
                ProviderMessageId = failed.ProviderMessageId,
                Reason = failed.Reason,
                CampaignId = callback.CampaignId,
                EventAtUtc = failed.OccurredAtUtc,
            };
}
