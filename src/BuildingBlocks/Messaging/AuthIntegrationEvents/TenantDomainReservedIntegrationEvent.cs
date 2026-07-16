namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al reservar temporalmente un slug de subdominio durante el alta
/// de una oficina nueva (Fase A7, ver ReserveSubdomainHandler). Cruza tenants por
/// diseño — todavía no existe un Tenant en este punto del flujo — así que TenantId
/// (heredado de IntegrationEvent) viaja como BuildingBlocks.Tenancy.PlatformTenant.Id.
/// </summary>
public sealed record TenantDomainReservedIntegrationEvent : IntegrationEvent
{
    public required string Slug { get; init; }
    public required string ReservedByEmail { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
