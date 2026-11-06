using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.SubscriptionIntegrationEvents.SubscriptionSeatReactivatedIntegrationEvent")]
public sealed record SubscriptionSeatReactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
}
