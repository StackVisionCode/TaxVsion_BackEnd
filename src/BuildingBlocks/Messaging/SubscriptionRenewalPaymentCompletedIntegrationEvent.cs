namespace BuildingBlocks.Messaging;

public sealed record SubscriptionRenewalPaymentCompletedIntegrationEvent : IntegrationEvent
{
    public Guid SubscriptionId { get; init; }
}
