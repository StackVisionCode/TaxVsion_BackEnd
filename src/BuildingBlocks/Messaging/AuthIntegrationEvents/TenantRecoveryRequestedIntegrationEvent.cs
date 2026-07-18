namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Una oficina a la que el email pertenece — nombre + host del subdominio activo.</summary>
public sealed record TenantRecoveryMatch(string TenantName, string Host);

/// <summary>
/// Publicado por Auth cuando "encuentra tu oficina" (Fase A4) encuentra >=1 tenant
/// para el email. Cruza tenants por diseño, así que TenantId (heredado de
/// IntegrationEvent) viaja como el tenant de plataforma (BuildingBlocks.Tenancy.PlatformTenant.Id),
/// no el de ninguna de las oficinas encontradas. Notification envía el correo con los
/// enlaces; nunca se lista en pantalla (anti-enumeración).
/// </summary>
public sealed record TenantRecoveryRequestedIntegrationEvent : IntegrationEvent
{
    public required string Email { get; init; }
    public required IReadOnlyList<TenantRecoveryMatch> Matches { get; init; }
}
