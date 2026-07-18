using BuildingBlocks.Persistence;

namespace TaxVision.Tenant.Application.Tenants.Abstractions;

public interface ITenantRepository : IRepository<TaxVision.Tenant.Domain.Tenant>
{
    Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default);
}
