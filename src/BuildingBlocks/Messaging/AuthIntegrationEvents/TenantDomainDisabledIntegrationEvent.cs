namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth al deshabilitar un dominio del tenant — Fase A6.</summary>
public sealed record TenantDomainDisabledIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string Host { get; init; }
}
