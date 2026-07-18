namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>Publicado por Tenant al borrar el logo de un tenant. Ver TenantLogoUpdatedIntegrationEvent.</summary>
public sealed record TenantLogoRemovedIntegrationEvent : IntegrationEvent
{
    public required DateTime RemovedAtUtc { get; init; }
}
