namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth cuando un dominio pasa a Active (subdominio wildcard inmediato, o custom hostname tras verificación) — Fase A6.</summary>
public sealed record TenantDomainActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string Host { get; init; }
}
