using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class TenantEntitlementSnapshotRepository(SubscriptionDbContext db) : ITenantEntitlementSnapshotRepository
{
    public Task<TenantEntitlementSnapshot?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.EntitlementSnapshots.AsNoTracking().FirstOrDefaultAsync(snapshot => snapshot.TenantId == tenantId, ct);

    public async Task UpsertAsync(
        TenantEntitlementSnapshot snapshot,
        long expectedPreviousRevision,
        CancellationToken ct = default
    )
    {
        var existing = await db.EntitlementSnapshots.FindAsync([snapshot.TenantId], ct);
        if (existing is null)
        {
            await db.EntitlementSnapshots.AddAsync(snapshot, ct);
            return;
        }

        var entry = db.Entry(existing);
        entry.CurrentValues.SetValues(snapshot);

        // SUB-5: el UPDATE exige la revisión sobre la que se calculó, no la que trajo FindAsync.
        entry.Property(row => row.RevisionNumber).OriginalValue = expectedPreviousRevision;
    }
}
