using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.TenantDomains.Events;

/// <summary>
/// Se emite al pasar a Active. Cubre tanto "verificado" como "activado" — en este
/// dominio ambos ocurren en el mismo instante (Cloudflare confirmó status=active &amp;
/// ssl.status=active, o el subdominio wildcard nace directo en Active).
/// </summary>
public sealed record TenantDomainActivated(Guid TenantId, Guid DomainId, string Host, Guid? ActingUserId)
    : IDomainEvent;
