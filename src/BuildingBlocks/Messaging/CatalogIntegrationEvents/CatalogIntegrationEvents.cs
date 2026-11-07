using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CatalogIntegrationEvents;

/// <summary>
/// Eventos del microservicio Catalog. Los consumidores (ej. un futuro Inventory, o Billing para
/// snapshotear líneas de factura) reaccionan sin que Catalog conozca su dominio. TenantId/CorrelationId
/// vienen de <see cref="IntegrationEvent"/>. El precio se publica como monto + moneda (multi-moneda).
/// <c>Created</c>, <c>Deactivated</c> y <c>TrackingChanged</c> van por <c>taxvision-events</c>.
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

/// <summary>
/// Contrato para un consumidor futuro (p. ej. Billing). Para escucharlo, ligar una cola a
/// <c>taxvision-catalog-items</c>: no viaja por <c>taxvision-events</c>.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.CatalogIntegrationEvents.CatalogItemUpdatedIntegrationEvent")]
public sealed record CatalogItemUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid ItemId { get; init; }
    public required string Name { get; init; }
    public required Guid CategoryId { get; init; }

    /// <summary>Opcional: <c>null</c> en mensajes de antes de CAT-1.</summary>
    public bool? TrackInventory { get; init; }

    /// <summary>Opcional: <c>null</c> en mensajes de antes de CAT-1.</summary>
    public bool? IsActive { get; init; }
}

/// <summary>
/// Contrato para un consumidor futuro (p. ej. Billing). Para escucharlo, ligar una cola a
/// <c>taxvision-catalog-items</c>: no viaja por <c>taxvision-events</c>.
/// </summary>
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

    /// <summary>
    /// Opcional: <c>null</c> en mensajes de antes de CAT-1. Con <c>true</c>, Inventory deja una marca si este evento
    /// llega antes que el <c>Created</c>.
    /// </summary>
    public bool? TrackInventory { get; init; }
}

/// <summary>
/// Cambió si el ítem se rastrea: se reactivó o se editó <c>TrackInventory</c>. El consumidor rastrea si
/// <c>TrackInventory &amp;&amp; IsActive</c>.
/// </summary>
[MessageIdentity("catalog.item.tracking_changed.v1")]
public sealed record CatalogItemTrackingChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ItemId { get; init; }
    public required bool TrackInventory { get; init; }
    public required bool IsActive { get; init; }
}
