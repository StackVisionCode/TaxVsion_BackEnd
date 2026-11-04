using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Avisa en la campana a quien envió un correo desde el CRM que no se pudo entregar. Un Delayed no avisa (D-4: el
/// servidor sigue intentando; si al final falla, llega como NotDelivered). El enlace abre la conversación.
/// </summary>
public static class CorrespondenceDeliveryNoticeReceivedConsumer
{
    public static async Task Handle(
        CorrespondenceDeliveryNoticeReceivedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.Outcome != CorrespondenceDeliveryOutcomes.NotDelivered)
                return;
            // Borradores previos a la autoría registrada: no hay a quién avisar.
            if (evt.SentByUserId == Guid.Empty)
                return;

            var title = $"Email to {evt.Recipient} couldn't be delivered";
            var resourceContext = new Dictionary<string, string>
            {
                ["accountId"] = evt.MailboxAccountId.ToString(),
                ["deliveryNoticeId"] = evt.DeliveryNoticeId.ToString(),
            };
            if (evt.CustomerId is { } customerId)
                resourceContext["customerId"] = customerId.ToString();

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.SentByUserId:N}",
                title,
                NotificationCategory.Collaboration,
                "correspondence.delivery_notice_received",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.SentByUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.CorrespondenceDeliveryFailed,
                    evt.SentByUserId,
                    evt.EventId,
                    "correspondence.delivery_notice_received.v1",
                    evt.OccurredOn,
                    title,
                    FallbackBody: "Open the conversation to see why and fix it.",
                    ResourceId: evt.EmailThreadId.ToString(),
                    ResourceContext: resourceContext,
                    Params: new Dictionary<string, string>
                    {
                        ["recipient"] = evt.Recipient,
                        ["category"] = evt.Category ?? string.Empty,
                    },
                    GroupKey: $"email_thread:{evt.EmailThreadId}"
                ),
                ct: ct
            );
        }
    }
}
