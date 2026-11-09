using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.SubscriptionIntegrationEvents.SubscriptionSeatSuspendedIntegrationEvent")]
public sealed record SubscriptionSeatSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
}
