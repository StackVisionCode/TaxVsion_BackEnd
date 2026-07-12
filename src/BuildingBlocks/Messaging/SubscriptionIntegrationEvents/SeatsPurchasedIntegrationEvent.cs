namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription/Billing al comprar asientos adicionales.</summary>
public sealed record SeatsPurchasedIntegrationEvent : IntegrationEvent
{
    public required Guid PurchasingTenantId { get; init; }
    public required int NewMaxUsers { get; init; }
}
