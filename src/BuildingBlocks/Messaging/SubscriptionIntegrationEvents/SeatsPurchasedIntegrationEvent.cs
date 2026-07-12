namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription al comprar asientos adicionales.</summary>
public sealed record SeatsPurchasedIntegrationEvent : IntegrationEvent
{
    public required Guid PurchasingTenantId { get; init; }
    public required int NewMaxUsers { get; init; }

    /// <summary>Ids de los seats creados en esta compra. Campo aditivo (Fase 2): los
    /// consumidores existentes que solo leen NewMaxUsers no necesitan cambiar nada.</summary>
    public Guid[] SeatIds { get; init; } = [];
}
