using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Infrastructure.Persistence;

namespace TaxVision.Reminder.Infrastructure.RateLimiting;

/// <summary>Lee la proyección local que mantiene <c>TenantPlanCodeProjectionConsumer</c>.</summary>
internal sealed class EfTenantPlanCodeReader(ReminderDbContext db) : ITenantPlanCodeReader
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
