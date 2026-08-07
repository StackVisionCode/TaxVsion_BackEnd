using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Domain.Backfill;

namespace TaxVision.Notes.Infrastructure.Persistence.Repositories;

public sealed class TenantBackfillStateRepository(NotesDbContext db) : ITenantBackfillStateRepository
{
    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantBackfillStates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBackfillState entity, CancellationToken ct = default) =>
        await db.TenantBackfillStates.AddAsync(entity, ct);
}
