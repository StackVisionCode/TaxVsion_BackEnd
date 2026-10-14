namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando el owner cancela una renovación antes de pagar.</summary>
public sealed record SubscriptionRenewalCheckoutCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid RenewalIntentId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? Reason { get; init; }
}
