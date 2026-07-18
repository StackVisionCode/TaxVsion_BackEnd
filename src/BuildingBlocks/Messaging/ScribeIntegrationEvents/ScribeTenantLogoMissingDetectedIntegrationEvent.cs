namespace BuildingBlocks.Messaging.ScribeIntegrationEvents;

/// <summary>
/// Publicado por Scribe cuando un render tenant-scoped necesita el logo del tenant pero no hay
/// TenantLogoRef (aún no subió uno o se le borró). Idempotente por TenantId + día — ver LogoResolver.
/// </summary>
public sealed record ScribeTenantLogoMissingDetectedIntegrationEvent : IntegrationEvent
{
    public required DateTime DetectedAtUtc { get; init; }
}
