using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth al deshabilitar un dominio del tenant — Fase A6.</summary>
[MessageIdentity("BuildingBlocks.Messaging.AuthIntegrationEvents.TenantDomainDisabledIntegrationEvent")]
public sealed record TenantDomainDisabledIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string Host { get; init; }
}
