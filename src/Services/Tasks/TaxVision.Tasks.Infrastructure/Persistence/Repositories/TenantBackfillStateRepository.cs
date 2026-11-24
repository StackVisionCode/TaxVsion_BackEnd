using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Domain.Backfill;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

public sealed class TenantBackfillStateRepository(TasksDbContext db) : ITenantBackfillStateRepository
{
    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .TenantBackfillStates.IgnoreQueryFilters(TaskQueryFilters.TenantOnly)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBackfillState entity, CancellationToken ct = default) =>
        await db.TenantBackfillStates.AddAsync(entity, ct);
}
