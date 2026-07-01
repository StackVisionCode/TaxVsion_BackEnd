namespace BuildingBlocks.Messaging;

public sealed record SeatPaymentCompletedIntegrationEvent : IntegrationEvent
{
    public Guid SeatSubscriptionId { get; init; }
}
