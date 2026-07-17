namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth cuando TenantHostResolutionMiddleware no logra resolver un Host
/// a un tenant activo (Host desconocido, tenant inactivo, dominio deshabilitado, etc).
/// Señal de seguridad para consumidores externos (alertas, SIEM) — el detalle forense
/// completo ya queda en AuthAuditLog (AuthAuditAction.TenantResolutionFailed); este
/// evento es solo para que otros servicios puedan reaccionar en tiempo real sin tener
/// que leer la tabla de auditoría de Auth. Cruza tenants por diseño (todavía no hay
/// tenant resuelto): TenantId (heredado de IntegrationEvent) viaja como
/// BuildingBlocks.Tenancy.PlatformTenant.Id.
/// </summary>
public sealed record TenantResolutionFailedIntegrationEvent : IntegrationEvent
{
    public required string Host { get; init; }
    public required string Reason { get; init; }
}
