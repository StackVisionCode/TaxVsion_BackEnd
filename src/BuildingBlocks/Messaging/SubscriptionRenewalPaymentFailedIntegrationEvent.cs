namespace BuildingBlocks.Messaging;

public sealed record SubscriptionRenewalPaymentFailedIntegrationEvent : IntegrationEvent
{
    public Guid SubscriptionId { get; init; }
    public string Reason { get; init; } = string.Empty;
}
