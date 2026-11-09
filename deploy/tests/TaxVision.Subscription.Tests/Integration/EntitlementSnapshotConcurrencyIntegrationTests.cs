using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>SUB-5: dos recálculos sobre la misma revisión. Sin la guarda, ambos escriben la 6 y gana el último.</summary>
public sealed class EntitlementSnapshotConcurrencyIntegrationTests : IAsyncLifetime
{
    private const long BaseRevision = 5;

    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task TwoRecalculationsFromSameRevision_SecondConflicts()
    {
        await SeedSnapshotAsync(BaseRevision, seatCount: 1);
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();

        await new TenantEntitlementSnapshotRepository(first).UpsertAsync(Snapshot(BaseRevision, 2), BaseRevision);
        await new TenantEntitlementSnapshotRepository(second).UpsertAsync(Snapshot(BaseRevision, 3), BaseRevision);
        await first.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        var stored = await ReadAsync();
        Assert.Equal(BaseRevision + 1, stored.RevisionNumber);
        Assert.Equal(2, stored.SeatCount);
    }

    [Fact]
    public async Task RetryOverNewRevision_WritesNextRevision()
    {
        await SeedSnapshotAsync(BaseRevision, seatCount: 1);
        await using (var first = _db.NewContext())
        {
            await new TenantEntitlementSnapshotRepository(first).UpsertAsync(Snapshot(BaseRevision, 2), BaseRevision);
            await first.SaveChangesAsync();
        }

        await using var retry = _db.NewContext();
        await new TenantEntitlementSnapshotRepository(retry).UpsertAsync(
            Snapshot(BaseRevision + 1, 3),
            BaseRevision + 1
        );
        await retry.SaveChangesAsync();

        var stored = await ReadAsync();
        Assert.Equal(BaseRevision + 2, stored.RevisionNumber);
        Assert.Equal(3, stored.SeatCount);
    }

    [Fact]
    public async Task TwoFirstSnapshots_SecondConflicts()
    {
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();

        await new TenantEntitlementSnapshotRepository(first).UpsertAsync(Snapshot(0, 2), 0);
        await new TenantEntitlementSnapshotRepository(second).UpsertAsync(Snapshot(0, 3), 0);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal(2, (await ReadAsync()).SeatCount);
    }

    private TenantEntitlementSnapshot Snapshot(long previousRevision, int seatCount) =>
        TenantEntitlementSnapshot
            .Rebuild(
                _db.TenantId,
                previousRevision,
                "starter",
                Guid.NewGuid(),
                "Active",
                seatCount,
                seatCount,
                [],
                DateTime.UtcNow
            )
            .Value;

    private async Task SeedSnapshotAsync(long revision, int seatCount)
    {
        await using var db = _db.NewContext();
        db.EntitlementSnapshots.Add(Snapshot(revision - 1, seatCount));
        await db.SaveChangesAsync();
    }

    private async Task<TenantEntitlementSnapshot> ReadAsync()
    {
        await using var db = _db.NewContext();
        return await db.EntitlementSnapshots.AsNoTracking().SingleAsync(s => s.TenantId == _db.TenantId);
    }
}
