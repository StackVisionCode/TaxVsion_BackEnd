namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al renombrar un subdominio de plataforma — Fase A7. Los servicios que guardan el
/// slug de la oficina (Tenant y sus réplicas) lo actualizan cuando <see cref="IsPrimary"/> es true: el
/// slug de una oficina es el de su subdominio primario. Los que cachean el host la descartan.
/// </summary>
public sealed record TenantSubdomainChangedIntegrationEvent : IntegrationEvent
{
    public required Guid DomainId { get; init; }
    public required string OldHost { get; init; }
    public required string NewHost { get; init; }
    public required string OldSlug { get; init; }
    public required string NewSlug { get; init; }

    /// <summary>Si es el subdominio primario de la oficina (el que define su slug).</summary>
    public required bool IsPrimary { get; init; }
}
