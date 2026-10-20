using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

[MessageIdentity("communication.call.missed.v1")]
public sealed record CallMissedIntegrationEvent : IntegrationEvent
{
    public required Guid CallId { get; init; }
    public required Guid CallerUserId { get; init; }
    public required Guid CalleeUserId { get; init; }
    public required string Kind { get; init; }
    public required DateTime RingingAtUtc { get; init; }
    public required DateTime MissedAtUtc { get; init; }
}
