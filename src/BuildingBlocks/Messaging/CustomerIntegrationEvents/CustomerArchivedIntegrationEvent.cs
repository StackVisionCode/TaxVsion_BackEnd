namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerArchivedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid ArchivedByUserId { get; init; }
}
