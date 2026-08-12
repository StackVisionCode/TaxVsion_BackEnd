using Microsoft.EntityFrameworkCore;
using TaxVision.Sms.Application.RateLimiting.Abstractions;
using TaxVision.Sms.Domain.RateLimiting;

namespace TaxVision.Sms.Infrastructure.Persistence.Repositories;

/// <summary>RateLimit Fase 2 — repo EF de la proyección local. Reads con IgnoreQueryFilters porque la
/// proyección es cross-tenant (la mantiene un consumer sin TenantContext ambiente).</summary>
public sealed class TenantPlanCodeProjectionRepository(SmsDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
