using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.RateLimiting.Abstractions;
using TaxVision.Customer.Domain.RateLimiting;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Persistence.Repositories;

// RateLimit Fase 6 — consumer Wolverine sin TenantContext ambiente (no hay HTTP request),
// mismo criterio que UserPermissionsProjectionRepository: IgnoreQueryFilters() explícito, el
// tenantId ya viene confiable desde el evento.
public sealed class TenantPlanCodeProjectionRepository(CustomerDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
