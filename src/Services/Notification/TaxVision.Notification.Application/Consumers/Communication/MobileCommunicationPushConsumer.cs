using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>Generic, content-free push. The device must reauthorize the referenced resource.</summary>
public static class MobileCommunicationPushConsumer
{
    public static async Task Handle(
        CallStartedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (
            evt.TenantId == Guid.Empty
            || evt.CalleeUserId == Guid.Empty
            || evt.CalleeUserId == evt.CallerUserId
            || evt.RingingAtUtc < clock.GetUtcNow().UtcDateTime.AddSeconds(-45)
        )
            return;
        var result = await dispatcher.SendPushAsync(
            new PushDispatchRequest(
                evt.TenantId,
                evt.CalleeUserId,
                "Incoming call",
                evt.Kind == "Video" ? "Your office is calling with video." : "You have an incoming call.",
                NotificationCategory.Collaboration,
                NotificationKinds.CommunicationCallIncoming,
                NotificationKinds.CommunicationCallIncoming,
                evt.EventId,
                evt.CorrelationId,
                Data: new Dictionary<string, string>
                {
                    ["section"] = "calls",
                    ["callId"] = evt.CallId.ToString(),
                    ["tenantId"] = evt.TenantId.ToString(),
                    ["recipientUserId"] = evt.CalleeUserId.ToString(),
                },
                Tag: $"call:{evt.CallId}",
                SkipWhenNoDevices: true
            ),
            ct
        );
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Code);
    }

    public static async Task Handle(
        ChatMessageSentIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        CancellationToken ct
    )
    {
        if (evt.TenantId == Guid.Empty || evt.Kind == "System")
            return;
        foreach (var userId in evt.RecipientUserIds.Where(id => id != Guid.Empty && id != evt.SenderId).Distinct())
        {
            var result = await dispatcher.SendPushAsync(
                new PushDispatchRequest(
                    evt.TenantId,
                    userId,
                    "New message",
                    "You have a new message.",
                    NotificationCategory.Collaboration,
                    NotificationKinds.CommunicationChatMessage,
                    NotificationKinds.CommunicationChatMessage,
                    evt.EventId,
                    evt.CorrelationId,
                    Data: new Dictionary<string, string>
                    {
                        ["section"] = "chat",
                        ["conversationId"] = evt.ConversationId.ToString(),
                        ["messageId"] = evt.MessageId.ToString(),
                        ["tenantId"] = evt.TenantId.ToString(),
                        ["recipientUserId"] = userId.ToString(),
                    },
                    Tag: $"chat:{evt.ConversationId}",
                    SkipWhenNoDevices: true
                ),
                ct
            );
            if (result.IsFailure)
                throw new InvalidOperationException(result.Error.Code);
        }
    }
}
