using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

internal sealed class TenantDirectoryRepository(PostmasterDbContext db) : ITenantDirectoryRepository
{
    public Task<TenantDirectoryEntry?> FindAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantDirectory.FirstOrDefaultAsync(e => e.TenantId == tenantId, ct);

    public async Task UpsertAsync(
        Guid tenantId,
        string name,
        string subDomain,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var existing = await db.TenantDirectory.FirstOrDefaultAsync(e => e.TenantId == tenantId, ct);
        if (existing is not null)
        {
            existing.Rename(name, subDomain, nowUtc);
            return;
        }

        await db.TenantDirectory.AddAsync(TenantDirectoryEntry.Create(tenantId, name, subDomain, nowUtc), ct);
    }

    public async Task<IReadOnlySet<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
        (await db.TenantDirectory.Select(e => e.TenantId).ToListAsync(ct)).ToHashSet();
}
