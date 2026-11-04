using Microsoft.EntityFrameworkCore;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Infrastructure.Persistence.Repositories;

/// <summary>TenantLogoRef no es ITenantOwned (sin filtro global), así que se consulta por TenantId
/// explícito sin IgnoreQueryFilters — seguro desde consumers/generación (scope Wolverine sin tenant).</summary>
public sealed class TenantLogoRefRepository(DocumentsDbContext db) : ITenantLogoRefRepository
{
    public Task<TenantLogoRef?> GetAsync(Guid tenantId, TenantLogoSurface surface, CancellationToken ct = default) =>
        db.TenantLogoRefs.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Surface == surface, ct);

    public async Task<IReadOnlyList<TenantLogoRef>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantLogoRefs.Where(x => x.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default) =>
        await db.TenantLogoRefs.AddAsync(logoRef, ct);
}
