using Wolverine.Attributes;
namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;
[MessageIdentity("communication.chat.message_sent.v1")]
public sealed record ChatMessageSentIntegrationEvent : IntegrationEvent
{
 public required Guid ConversationId {get;init;}
 public required Guid MessageId {get;init;}
 public required Guid SenderId {get;init;}
 public required string Kind {get;init;}
 public required IReadOnlyList<Guid> RecipientUserIds {get;init;}
 public required DateTime SentAtUtc {get;init;}
}
