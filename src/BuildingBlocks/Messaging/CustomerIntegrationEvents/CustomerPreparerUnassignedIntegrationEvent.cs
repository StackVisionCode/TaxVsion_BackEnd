namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerPreparerUnassignedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid UnassignedByUserId { get; init; }
}
