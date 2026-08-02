using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.RateLimiting.Abstractions;
using TaxVision.Signature.Domain.RateLimiting;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

// RateLimit Fase 2 — consumer Wolverine sin TenantContext ambiente (no hay HTTP request), mismo
// criterio que AuthzUserPermissionsProjectionRepository: IgnoreQueryFilters() explícito, el
// tenantId ya viene confiable desde el evento.
public sealed class TenantPlanCodeProjectionRepository(SignatureDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
