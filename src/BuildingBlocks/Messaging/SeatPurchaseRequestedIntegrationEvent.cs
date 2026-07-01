namespace BuildingBlocks.Messaging;

public sealed record SeatPurchaseRequestedIntegrationEvent : IntegrationEvent
{
    public Guid SeatSubscriptionId { get; init; }
    public Guid SubscriptionId { get; init; }
    public int Quantity { get; init; }
    public long AmountCents { get; init; }
    public string Currency { get; init; } = "USD";
}
