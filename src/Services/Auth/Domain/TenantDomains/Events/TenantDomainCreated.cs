using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.TenantDomains.Events;

/// <summary>Se emite al crear un TenantDomain — subdominio wildcard o custom hostname, manual o automático (backfill/onboarding).</summary>
public sealed record TenantDomainCreated(
    Guid TenantId,
    Guid DomainId,
    string Host,
    string DomainType,
    Guid? ActingUserId
) : IDomainEvent;
