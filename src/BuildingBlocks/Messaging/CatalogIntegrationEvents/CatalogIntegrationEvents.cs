using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CatalogIntegrationEvents;

/// <summary>
/// Eventos del microservicio Catalog. Los consumidores (ej. un futuro Inventory, o Billing para
/// snapshotear líneas de factura) reaccionan sin que Catalog conozca su dominio. TenantId/CorrelationId
/// vienen de <see cref="IntegrationEvent"/>. El precio se publica como monto + moneda (multi-moneda).
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.CatalogIntegrationEvents.CatalogItemCreatedIntegrationEvent")]
public sealed record CatalogItemCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid ItemId { get; init; }
    public required Guid CategoryId { get; init; }
    public required string Name { get; init; }
    public string? Sku { get; init; }
    public required string Kind { get; init; }
    public required bool TrackInventory { get; init; }
    public required decimal UnitPrice { get; init; }
    public required string Currency { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.CatalogIntegrationEvents.CatalogItemUpdatedIntegrationEvent")]
public sealed record CatalogItemUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid ItemId { get; init; }
    public required string Name { get; init; }
    public required Guid CategoryId { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.CatalogIntegrationEvents.CatalogItemPriceChangedIntegrationEvent")]
public sealed record CatalogItemPriceChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ItemId { get; init; }
    public required decimal UnitPrice { get; init; }
    public required string Currency { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.CatalogIntegrationEvents.CatalogItemDeactivatedIntegrationEvent")]
public sealed record CatalogItemDeactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid ItemId { get; init; }
}
