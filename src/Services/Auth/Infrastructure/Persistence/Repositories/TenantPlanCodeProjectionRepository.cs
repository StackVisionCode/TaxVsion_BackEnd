using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.RateLimiting.Abstractions;
using TaxVision.Auth.Domain.RateLimiting;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

// RateLimit Fase 2 — consumer Wolverine sin TenantContext ambiente (no hay HTTP request), mismo
// criterio que el resto de los servicios: IgnoreQueryFilters() explícito, el tenantId ya viene
// confiable desde el evento.
public sealed class TenantPlanCodeProjectionRepository(AuthDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
