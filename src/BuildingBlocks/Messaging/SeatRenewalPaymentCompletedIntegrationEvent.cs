namespace BuildingBlocks.Messaging;

public sealed record SeatRenewalPaymentCompletedIntegrationEvent : IntegrationEvent
{
    public Guid SeatSubscriptionId { get; init; }
}
