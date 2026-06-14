using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;
namespace TaxVision.Tenant.Infrastructure.Persistence.Repositories;

// Implementación concreta del repositorio sobre EF Core.
public sealed class TenantRepository(TenantDbContext db) : ITenantRepository
{
    public Task<DomainTenant?> GetByIdAsync(Guid id, CancellationToken ct = default)
     => db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
    public async Task AddAsync(DomainTenant entity, CancellationToken ct = default)
    {
        _ = await db.Tenants.AddAsync(entity, ct);
    }
    public void Remove(DomainTenant entity) => db.Tenants.Remove(entity);

    public async Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) => (bool)await db.Tenants.AnyAsync(t => string.Equals(t.SubDomain, subdomain.ToLower()), ct);
}
