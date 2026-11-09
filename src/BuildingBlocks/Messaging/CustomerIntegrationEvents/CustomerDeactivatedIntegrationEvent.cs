using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerDeactivatedIntegrationEvent")]
public sealed record CustomerDeactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid DeactivatedByUserId { get; init; }
    public required DateTime DeactivatedAtUtc { get; init; }
}
