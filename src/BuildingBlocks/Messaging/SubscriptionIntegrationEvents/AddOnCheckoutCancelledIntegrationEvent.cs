namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando el usuario cancela una compra de add-on antes de pagar.</summary>
public sealed record AddOnCheckoutCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid AddOnPurchaseIntentId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? Reason { get; init; }
}
