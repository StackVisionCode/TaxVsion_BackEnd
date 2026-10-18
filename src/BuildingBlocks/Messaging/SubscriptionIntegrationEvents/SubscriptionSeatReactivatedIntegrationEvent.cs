namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

public sealed record SubscriptionSeatReactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
}
