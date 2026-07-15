namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth al crear un dominio (custom hostname) del tenant — Fase A6.</summary>
public sealed record TenantDomainCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string Host { get; init; }
    public required string DomainType { get; init; }
}
