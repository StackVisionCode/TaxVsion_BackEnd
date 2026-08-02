using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.RateLimiting.Abstractions;
using TaxVision.PaymentClient.Domain.RateLimiting;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

// RateLimit Fase 2 — consumer Wolverine sin TenantContext ambiente (no hay HTTP request), mismo
// criterio que UserPermissionsProjectionRepository: IgnoreQueryFilters() explícito, el tenantId ya
// viene confiable desde el evento.
public sealed class TenantPlanCodeProjectionRepository(PaymentClientDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
