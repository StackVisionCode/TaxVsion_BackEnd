namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth al renombrar el subdominio primario de un tenant — Fase A7.</summary>
public sealed record TenantSubdomainChangedIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string OldHost { get; init; }
    public required string NewHost { get; init; }
}
