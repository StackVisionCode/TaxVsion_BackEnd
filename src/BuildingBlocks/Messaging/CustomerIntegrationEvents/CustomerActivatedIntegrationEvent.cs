namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid ActivatedByUserId { get; init; }
    public required DateTime ActivatedAtUtc { get; init; }
}
