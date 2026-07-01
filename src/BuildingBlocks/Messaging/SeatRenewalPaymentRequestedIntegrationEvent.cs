namespace BuildingBlocks.Messaging;

public sealed record SeatRenewalPaymentRequestedIntegrationEvent : IntegrationEvent
{
    public Guid SeatSubscriptionId { get; init; }
    public long AmountCents { get; init; }
    public string Currency { get; init; } = "USD";
}
