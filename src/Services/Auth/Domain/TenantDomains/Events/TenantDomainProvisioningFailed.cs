using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.TenantDomains.Events;

/// <summary>Se emite cuando Cloudflare bloquea/rechaza un custom hostname ya en Provisioning (el poller es hoy el único disparador).</summary>
public sealed record TenantDomainProvisioningFailed(
    Guid TenantId,
    Guid DomainId,
    string Host,
    string Reason,
    Guid? ActingUserId
) : IDomainEvent;
