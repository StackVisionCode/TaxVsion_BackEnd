using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerPreparerUnassignedIntegrationEvent")]
public sealed record CustomerPreparerUnassignedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid UnassignedByUserId { get; init; }
}
