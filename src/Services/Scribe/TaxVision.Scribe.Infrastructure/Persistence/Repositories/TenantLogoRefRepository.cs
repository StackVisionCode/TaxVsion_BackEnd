using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class TenantLogoRefRepository(ScribeDbContext dbContext) : ITenantLogoRefRepository
{
    // IgnoreQueryFilters: llamado exclusivamente desde el pipeline de render M2M (LogoResolver ←
    // RenderController, ActorType.Service — sin claim tenant_id, el ITenantContext ambiente nunca
    // refleja el tenant real del render). El aislamiento acá lo da el parámetro tenantId explícito
    // de la query, no el filtro global (RBAC Fase 5).
    public Task<TenantLogoRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.TenantLogoRefs.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);

    public async Task AddAsync(TenantLogoRef logoRef, CancellationToken ct = default) =>
        await dbContext.TenantLogoRefs.AddAsync(logoRef, ct);
}
