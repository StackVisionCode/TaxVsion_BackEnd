using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.RateLimiting.Abstractions;
using TaxVision.Mobile.Domain.RateLimiting;

namespace TaxVision.Mobile.Infrastructure.Persistence.Repositories;

// Filas con seguimiento para que el consumer las modifique; tenant siempre explícito.
public sealed class TenantPlanCodeProjectionRepository(MobileDbContext db) : ITenantPlanCodeProjectionRepository
{
    public Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);

    public async Task<bool> TryAddAndSaveAsync(TenantPlanCodeProjection projection, CancellationToken ct = default)
    {
        db.TenantPlanCodeProjections.Add(projection);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (BuildingBlocks.Results.ConflictException ex) when (ex.Code == "Persistence.UniqueConstraint")
        {
            db.Entry(projection).State = EntityState.Detached;
            return false;
        }
    }
}
