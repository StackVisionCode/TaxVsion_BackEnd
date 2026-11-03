namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>Publicado por Tenant al borrar el logo de un tenant. Ver TenantLogoUpdatedIntegrationEvent.</summary>
public sealed record TenantLogoRemovedIntegrationEvent : IntegrationEvent
{
    public required DateTime RemovedAtUtc { get; init; }

    // Ausente en mensajes viejos = Crm (antes solo se publicaba el logo del CRM).
    public string Surface { get; init; } = TenantBrandSurfaces.Crm;
}
