namespace BuildingBlocks.Messaging.PlatformIntegrationEvents;

/// <summary>
/// Publicado por Tenant cada vez que cambia <c>PlatformSettings</c> (marca, nombre legal, dominio,
/// host de la API, correos de contacto). Lleva la foto COMPLETA, no un diff: el consumer la aplica
/// solo si <see cref="Version"/> es mayor que la que ya tiene, así que reordenar o repetir el mensaje
/// no hace daño. <c>TenantId</c> es siempre el tenant Platform.
/// </summary>
public sealed record PlatformSettingsChangedIntegrationEvent : IntegrationEvent
{
    public required long Version { get; init; }
    public required string BrandName { get; init; }
    public required string LegalName { get; init; }
    public required string BaseDomain { get; init; }
    public required string ApiHost { get; init; }
    public required string SupportEmail { get; init; }
    public required string BillingEmail { get; init; }
    public required DateTime ChangedAtUtc { get; init; }
}
