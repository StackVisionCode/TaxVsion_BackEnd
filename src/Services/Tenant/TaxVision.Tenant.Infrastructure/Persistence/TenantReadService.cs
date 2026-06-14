using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
namespace TaxVision.Tenant.Infrastructure.Persistence;

public sealed class TenantReadService(TenantDbContext db) : ITenantReadService
{
    public async Task<IReadOnlyList<TenantResponse>> GetPageAsync(int page, int size, CancellationToken ct = default)
    => (IReadOnlyList<TenantResponse>)await db.Tenants
 .AsNoTracking()
 .OrderBy(t => t.Name)
 .Skip((page - 1) * size)
 .Take(size)
 .Select(t => new TenantResponse(t.Id, t.Name, t.SubDomain))
 .ToListAsync(ct);



}
