using Wolverine.Attributes;
namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;
[MessageIdentity("communication.call.started.v1")]
public sealed record CallStartedIntegrationEvent : IntegrationEvent
{
 public required Guid CallId {get;init;}
 public required Guid CallerUserId {get;init;}
 public required Guid CalleeUserId {get;init;}
 public required string Kind {get;init;}
 public Guid? ConversationId {get;init;}
 public required DateTime RingingAtUtc {get;init;}
}
