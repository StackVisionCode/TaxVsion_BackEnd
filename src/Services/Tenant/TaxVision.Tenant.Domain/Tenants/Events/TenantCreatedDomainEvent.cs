using BuildingBlocks.Domain;

namespace TaxVision.Tenant.Domain.Tenants.Events;

public sealed record TenantCreatedDomainEvent(Guid TenantId, string SubDomain) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

}

public sealed record TenantSuspendedDomainEvent(Guid TenantId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

}