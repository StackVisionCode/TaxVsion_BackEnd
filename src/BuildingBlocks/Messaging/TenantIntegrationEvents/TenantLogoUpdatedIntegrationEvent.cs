namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// Publicado por Tenant cuando se confirma el logo de una superficie (<see cref="Surface"/>: CRM o
/// Portal). Scribe, Documents y Signature lo proyectan; lo que ve el cliente usa el del Portal y cae
/// al del CRM si no hay.
/// </summary>
public sealed record TenantLogoUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid CloudStorageFileId { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }

    // Ausente en mensajes viejos = Crm (antes solo se publicaba el logo del CRM).
    public string Surface { get; init; } = TenantBrandSurfaces.Crm;
}
