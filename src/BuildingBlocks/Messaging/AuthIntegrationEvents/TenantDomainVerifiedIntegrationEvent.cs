namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth cuando Cloudflare confirma DNS/TLS validados para un custom hostname — Fase A6.</summary>
public sealed record TenantDomainVerifiedIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string Host { get; init; }
}
