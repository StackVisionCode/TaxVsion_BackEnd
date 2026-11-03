using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class TenantLogoRefRepository(ScribeDbContext dbContext) : ITenantLogoRefRepository
{
    // IgnoreQueryFilters: llamado desde el pipeline de render M2M (LogoResolver ← RenderController,
    // ActorType.Service — sin claim tenant_id) y desde consumers. El aislamiento lo da el tenantId
    // explícito de la query, no el filtro global (RBAC Fase 5).
    public Task<TenantLogoRef?> GetAsync(Guid tenantId, TenantLogoSurface surface, CancellationToken ct = default) =>
        dbContext
            .TenantLogoRefs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Surface == surface, ct);

    public async Task<IReadOnlyList<TenantLogoRef>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        await dbContext.TenantLogoRefs.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default) =>
        await dbContext.TenantLogoRefs.AddAsync(logoRef, ct);
}
