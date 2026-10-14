namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por el sweeper cuando una intención de compra de add-on vence sin pago.</summary>
public sealed record AddOnCheckoutExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid AddOnPurchaseIntentId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
}
