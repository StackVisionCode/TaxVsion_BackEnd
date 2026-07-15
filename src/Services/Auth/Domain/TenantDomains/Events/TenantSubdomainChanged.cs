using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.TenantDomains.Events;

/// <summary>Se emite al renombrar el subdominio primario de un tenant ya activo (Fase A7).</summary>
public sealed record TenantSubdomainChanged(
    Guid TenantId,
    Guid DomainId,
    string OldHost,
    string NewHost,
    Guid? ActingUserId
) : IDomainEvent;
