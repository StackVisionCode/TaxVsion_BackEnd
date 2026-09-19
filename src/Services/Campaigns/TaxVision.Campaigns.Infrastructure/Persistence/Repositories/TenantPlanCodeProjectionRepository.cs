using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.RateLimiting.Abstractions;
using TaxVision.Campaigns.Domain.RateLimiting;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

// Consumer Wolverine sin TenantContext ambiente (no hay HTTP request), mismo criterio que
// UserPermissionsProjectionRepository: IgnoreQueryFilters() explícito, el tenantId ya viene
// confiable desde el evento.
public sealed class TenantPlanCodeProjectionRepository(CampaignsDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
