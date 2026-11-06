using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando el usuario cancela una compra de asientos antes de pagar.</summary>
[MessageIdentity("BuildingBlocks.Messaging.SubscriptionIntegrationEvents.SeatCheckoutCancelledIntegrationEvent")]
public sealed record SeatCheckoutCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid SeatPurchaseIntentId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? Reason { get; init; }
}
