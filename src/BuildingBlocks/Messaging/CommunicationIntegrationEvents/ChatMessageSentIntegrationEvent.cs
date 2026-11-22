using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

[MessageIdentity("communication.chat.message_sent.v1")]
public sealed record ChatMessageSentIntegrationEvent : IntegrationEvent
{
    public required Guid ConversationId { get; init; }
    public required Guid MessageId { get; init; }
    public required Guid SenderId { get; init; }
    public required string Kind { get; init; }
    public required IReadOnlyList<Guid> RecipientUserIds { get; init; }
    public required DateTime SentAtUtc { get; init; }

    // Opcionales: un evento viejo sin ellos se trata como hoy.
    public string? ConversationKind { get; init; }
    public Guid? MeetingId { get; init; }
    public Guid? SupportTicketId { get; init; }

    // A quién conviene avisar por push (sin silenciados ni quien ya lo ve); null = a todos.
    public IReadOnlyList<Guid>? PushRecipientUserIds { get; init; }
}
