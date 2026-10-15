namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por el sweeper cuando una renovación por checkout vence sin pago.</summary>
public sealed record SubscriptionRenewalCheckoutExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid RenewalIntentId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
}
