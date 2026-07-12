namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>Publicado por Subscription cuando un tenant compra/activa un add-on.</summary>
public sealed record AddOnActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantAddOnId { get; init; }
    public required string AddOnCode { get; init; }
    public required int Quantity { get; init; }
    public required DateTime CurrentPeriodEndUtc { get; init; }
}
