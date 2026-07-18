using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Backfill;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class TenantBackfillStateRepository(CorrespondenceDbContext db) : ITenantBackfillStateRepository
{
    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantBackfillStates.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBackfillState entity, CancellationToken ct = default)
    {
        await db.TenantBackfillStates.AddAsync(entity, ct);
    }

    public async Task<IReadOnlyList<Guid>> ListAllTenantIdsAsync(CancellationToken ct = default) =>
        await db.TenantBackfillStates.AsNoTracking().Select(x => x.TenantId).ToListAsync(ct);
}
