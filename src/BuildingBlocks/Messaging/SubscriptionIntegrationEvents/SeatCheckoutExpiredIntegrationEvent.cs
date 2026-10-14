namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por el sweeper cuando una intención de compra de asientos vence sin pago.</summary>
public sealed record SeatCheckoutExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid SeatPurchaseIntentId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
}
