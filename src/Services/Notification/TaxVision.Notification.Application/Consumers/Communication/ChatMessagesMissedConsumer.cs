using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// D-N4: "mensajes perdidos". Una fila por conversación (GroupKey <c>chat:{id}</c>) que abre el chat;
/// Communication la marca leída cuando el usuario lee la conversación. Sin extracto del mensaje.
/// </summary>
public static class ChatMessagesMissedConsumer
{
    public static async Task Handle(
        ChatMessagesMissedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.TenantId == Guid.Empty || evt.RecipientUserId == Guid.Empty || evt.UnreadCount <= 0)
                return;

            const string title = "New messages";
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CommunicationChatMessagesMissed,
                    evt.TenantId,
                    new NotificationTarget(evt.RecipientUserId),
                    evt.EventId,
                    "communication.chat.messages_missed.v1",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: new Dictionary<string, string> { ["count"] = evt.UnreadCount.ToString() },
                        ResourceId: evt.ConversationId.ToString(),
                        GroupKey: $"chat:{evt.ConversationId:N}",
                        ActorUserId: evt.LatestSenderId,
                        FallbackTitle: title,
                        FallbackBody: "You have unread messages in a conversation."
                    )
                ),
                ct
            );
        }
    }
}
