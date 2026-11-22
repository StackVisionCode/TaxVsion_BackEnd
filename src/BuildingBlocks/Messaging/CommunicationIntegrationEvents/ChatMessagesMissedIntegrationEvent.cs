using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Mensajes que siguen sin leer pasados N minutos (Communication, una vez por racha). Solo ids y un contador.
/// </summary>
[MessageIdentity("communication.chat.messages_missed.v1")]
public sealed record ChatMessagesMissedIntegrationEvent : IntegrationEvent
{
    public required Guid ConversationId { get; init; }
    public required string ConversationKind { get; init; }
    public required Guid RecipientUserId { get; init; }
    public required int UnreadCount { get; init; }
    public required DateTime OldestUnreadAtUtc { get; init; }
    public required Guid LatestMessageId { get; init; }
    public required Guid LatestSenderId { get; init; }
}
