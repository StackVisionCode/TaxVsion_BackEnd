namespace BuildingBlocks.Messaging;

public sealed record SeatRenewalPaymentFailedIntegrationEvent : IntegrationEvent
{
    public Guid SeatSubscriptionId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
