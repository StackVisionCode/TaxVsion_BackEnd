namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// Publicado por Tenant al subir/reemplazar el logo de un tenant (ver
/// Tenant_Service_LogoSupport_Plan.md — diseño aprobado, aún no implementado del lado de Tenant).
/// Scribe consume este evento para alimentar su proyección local TenantLogoRef (Fase 4.5).
/// </summary>
public sealed record TenantLogoUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid CloudStorageFileId { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}
