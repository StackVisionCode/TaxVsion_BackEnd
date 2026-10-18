namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

public sealed record SubscriptionSeatSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
}
