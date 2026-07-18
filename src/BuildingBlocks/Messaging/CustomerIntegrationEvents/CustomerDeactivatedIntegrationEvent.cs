namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

public sealed record CustomerDeactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid DeactivatedByUserId { get; init; }
    public required DateTime DeactivatedAtUtc { get; init; }
}
