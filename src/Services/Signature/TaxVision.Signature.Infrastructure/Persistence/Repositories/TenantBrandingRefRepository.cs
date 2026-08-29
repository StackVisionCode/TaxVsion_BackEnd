using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

internal sealed class TenantBrandingRefRepository(SignatureDbContext db) : ITenantBrandingRefRepository
{
    // tenantId explícito y validado por el consumer/sealing — IgnoreQueryFilters porque el filtro
    // ambiental global puede no estar poblado en este scope de DI (mismo patrón que las demás proyecciones).
    public Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantBrandingRefs.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default) =>
        await db.TenantBrandingRefs.AddAsync(branding, ct);
}
