using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>Generic, content-free push. The device must reauthorize the referenced resource.</summary>
public static class MobileCommunicationPushConsumer
{
    public static async Task Handle(
        CallStartedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ILogger<CallStartedIntegrationEvent> logger,
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

        var summary = await dispatcher.DispatchAsync(
            new NotificationIntent(
                NotificationKinds.CommunicationCallIncoming,
                evt.TenantId,
                new NotificationTarget(evt.CalleeUserId),
                evt.EventId,
                "communication.call.started.v1",
                evt.RingingAtUtc,
                evt.CorrelationId,
                Push: new PushContent(
                    evt.ConversationId?.ToString(),
                    Tag: $"call:{evt.CallId}",
                    // Una llamada sin contestar no sirve pasado el timbre.
                    TimeToLive: TimeSpan.FromSeconds(45),
                    Data: new Dictionary<string, string> { ["section"] = "calls", ["callId"] = evt.CallId.ToString() },
                    TextKey: evt.Kind == "Video" ? PushText.VideoCall : null,
                    SkipWhenNoDevices: true,
                    LinkForRecipient: true,
                    // Sin conversación no hay a dónde llevar el clic: el push sale sin enlace.
                    WithoutLink: evt.ConversationId is null,
                    Inline: true
                )
            ),
            ct
        );
        if (TransientError(summary, evt.CalleeUserId, logger) is { } code)
            throw new InvalidOperationException(code);
    }

    // N8-03: el evento solo se pasa al carril masivo; un pico de chat no ocupa notification-events.
    public static DispatchChatPush? Handle(ChatMessageSentIntegrationEvent evt) =>
        evt.TenantId == Guid.Empty
        || evt.Kind == "System"
        || string.Equals(evt.ConversationKind, "Meeting", StringComparison.OrdinalIgnoreCase)
            ? null
            : new DispatchChatPush(
                evt.TenantId,
                evt.EventId,
                evt.ConversationId,
                evt.MessageId,
                evt.SenderId,
                evt.RecipientUserIds,
                evt.PushRecipientUserIds,
                evt.SentAtUtc,
                evt.CorrelationId
            );

    // Encola un push por destinatario (N8-04): el reintento por fallo transitorio lo hace la cola de push.
    public static async Task Handle(DispatchChatPush command, NotificationDispatcher dispatcher, CancellationToken ct)
    {
        // Communication ya quitó silenciados y a quien lo está viendo; sin lista (evento viejo) se avisa a todos.
        var pushTo = command.PushRecipientUserIds is { } allowed ? allowed.ToHashSet() : null;
        foreach (
            var userId in command
                .RecipientUserIds.Where(id =>
                    id != Guid.Empty && id != command.SenderId && (pushTo?.Contains(id) ?? true)
                )
                .Distinct()
        )
        {
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CommunicationChatMessage,
                    command.TenantId,
                    new NotificationTarget(userId),
                    command.EventId,
                    "communication.chat.message_sent.v1",
                    command.SentAtUtc,
                    command.CorrelationId,
                    Push: new PushContent(
                        command.ConversationId.ToString(),
                        Tag: $"chat:{command.ConversationId}",
                        Data: new Dictionary<string, string>
                        {
                            ["section"] = "chat",
                            ["conversationId"] = command.ConversationId.ToString(),
                            ["messageId"] = command.MessageId.ToString(),
                        },
                        SkipWhenNoDevices: true,
                        LinkForRecipient: true
                    )
                ),
                ct
            );
        }
    }

    // Solo un fallo transitorio se reintenta; el resto se registra y no frena a los demás destinatarios.
    private static string? TransientError(DispatchSummary summary, Guid userId, ILogger logger)
    {
        if (summary.Errors?.GetValueOrDefault(NotificationChannel.Push) is not { } error)
            return null;
        if (PushErrorCodes.IsTransient(error.Code))
            return error.Code;
        logger.LogWarning("Push to user {UserId} was not sent: {Code}", userId, error.Code);
        return null;
    }
}
