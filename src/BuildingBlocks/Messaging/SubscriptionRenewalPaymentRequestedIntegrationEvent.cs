namespace BuildingBlocks.Messaging;

public sealed record SubscriptionRenewalPaymentRequestedIntegrationEvent : IntegrationEvent
{
    public Guid SubscriptionId { get; init; }
    public long AmountCents { get; init; }
    public string Currency { get; init; } = "USD";
}
