using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.RateLimiting.Abstractions;
using TaxVision.Scribe.Domain.RateLimiting;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

// RateLimit Fase 2 — consumer Wolverine sin TenantContext ambiente (no hay HTTP request), mismo
// criterio que UserPermissionsProjectionRepository: IgnoreQueryFilters() explícito, el tenantId ya
// viene confiable desde el evento.
public sealed class TenantPlanCodeProjectionRepository(ScribeDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
