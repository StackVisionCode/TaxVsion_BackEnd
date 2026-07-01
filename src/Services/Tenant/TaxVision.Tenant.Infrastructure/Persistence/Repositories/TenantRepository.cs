using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Infrastructure.Persistence.Repositories;

public sealed class TenantRepository(TenantDbContext db) : ITenantRepository
{
    public Task<DomainTenant?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(DomainTenant entity, CancellationToken ct = default)
    {
        await db.Tenants.AddAsync(entity, ct);
    }

    public void Remove(DomainTenant entity)
        => db.Tenants.Remove(entity);

    public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default)
    {
        var normalized = subdomain.Trim().ToLowerInvariant();
        return db.Tenants.AnyAsync(t => t.SubDomain == normalized, ct);
    }
}
