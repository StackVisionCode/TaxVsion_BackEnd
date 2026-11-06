using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerPreparerAssignedIntegrationEvent")]
public sealed record CustomerPreparerAssignedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid PreparerUserId { get; init; }
    public required Guid AssignedByUserId { get; init; }
}
