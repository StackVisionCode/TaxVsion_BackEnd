using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Domain.Backfill;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

public sealed class TenantBackfillStateRepository(CalendarDbContext db) : ITenantBackfillStateRepository
{
    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantBackfillStates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBackfillState entity, CancellationToken ct = default) =>
        await db.TenantBackfillStates.AddAsync(entity, ct);
}
