using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Infrastructure.Persistence;

namespace TaxVision.Notes.Infrastructure.RateLimiting;

/// <summary>RateLimit Fase 4 — lee la proyección local mantenida por TenantPlanCodeProjectionConsumer.</summary>
internal sealed class EfTenantPlanCodeReader(NotesDbContext db) : ITenantPlanCodeReader
{
    public async Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default)
    {
        var projection = await db
            .TenantPlanCodeProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        return projection?.PlanCode;
    }
}
