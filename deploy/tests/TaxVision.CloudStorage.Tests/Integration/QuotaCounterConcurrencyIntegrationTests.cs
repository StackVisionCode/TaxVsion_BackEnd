using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (user-secrets de CloudStorage o CLOUDSTORAGE_DB_CONNECTION): dos escaneos
/// que terminan a la vez comprometen cuota sobre la misma fila de TenantStorageLimits. Antes el segundo
/// guardado tiraba DbUpdateConcurrencyException; ahora se fusionan los contadores y el total cuadra.
/// </summary>
public sealed class QuotaCounterConcurrencyIntegrationTests : IAsyncLifetime
{
    private const long Gigabyte = 1024L * 1024 * 1024;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString = CloudStorageIntegrationDatabase.ConnectionString;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        var limit = TenantStorageLimit.Create(_tenantId, "starter", 10 * Gigabyte, Gigabyte);
        Assert.True(limit.Reserve(100 + 200 + 8 * 50).IsSuccess);
        db.StorageLimits.Add(limit);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.StorageLimits.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private async Task<TenantStorageLimit> ReadAsync()
    {
        await using var db = NewContext();
        return await db.StorageLimits.AsNoTracking().SingleAsync(l => l.TenantId == _tenantId);
    }

    [Fact]
    public async Task Two_commits_from_stale_reads_add_up_instead_of_failing()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var b = await second.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);

        a.Commit(100);
        b.Commit(200);
        await first.SaveChangesAsync();
        await second.SaveChangesAsync(); // misma RowVersion leída: antes, DbUpdateConcurrencyException

        var final = await ReadAsync();
        Assert.Equal(300, final.UsedBytes);
        Assert.Equal(8 * 50, final.ReservedBytes);
    }

    [Fact]
    public async Task TwoReservationsBeyondMax_SecondFailsWithQuotaExceeded()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var b = await second.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var seeded = a.ReservedBytes;

        Assert.True(a.Reserve(6 * Gigabyte).IsSuccess);
        Assert.True(b.Reserve(6 * Gigabyte).IsSuccess);
        await first.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Storage.QuotaExceeded", conflict.Code);
        Assert.Equal(seeded + 6 * Gigabyte, (await ReadAsync()).ReservedBytes);
    }

    [Fact]
    public async Task A_reservation_that_still_fits_keeps_merging()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var b = await second.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var seeded = a.ReservedBytes;

        Assert.True(a.Reserve(4 * Gigabyte).IsSuccess);
        Assert.True(b.Reserve(4 * Gigabyte).IsSuccess);
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();

        Assert.Equal(seeded + 8 * Gigabyte, (await ReadAsync()).ReservedBytes);
    }

    [Fact]
    public async Task A_release_over_a_full_quota_keeps_merging()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var b = await second.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var seeded = a.ReservedBytes;

        Assert.True(a.Reserve(10 * Gigabyte - seeded).IsSuccess);
        b.Release(100);
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();

        Assert.Equal(10 * Gigabyte - 100, (await ReadAsync()).ReservedBytes);
    }

    [Fact]
    public async Task An_older_snapshot_that_loses_the_race_does_not_overwrite_the_newer_one()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var newer = await first.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var older = await second.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);

        newer.ApplyEntitlements(5, "pro", 20 * Gigabyte, Gigabyte, null, isSuspended: false);
        older.ApplyEntitlements(4, "starter", 10 * Gigabyte, Gigabyte, null, isSuspended: true);
        await first.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        var final = await ReadAsync();
        Assert.Equal(5, final.LastEntitlementsRevision);
        Assert.Equal("pro", final.PlanCode);
        Assert.False(final.IsSuspended);
    }

    [Fact]
    public async Task A_quota_write_that_races_a_snapshot_merges_without_conflict()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var scan = await first.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
        var snapshot = await second.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);

        scan.Commit(100);
        snapshot.ApplyEntitlements(7, "pro", 20 * Gigabyte, Gigabyte, null, isSuspended: false);
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();

        var final = await ReadAsync();
        Assert.Equal(100, final.UsedBytes);
        Assert.Equal(7, final.LastEntitlementsRevision);
    }

    [Fact]
    public async Task Eight_parallel_commits_keep_every_byte()
    {
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async _ =>
                {
                    await using var db = NewContext();
                    var limit = await db.StorageLimits.SingleAsync(l => l.TenantId == _tenantId);
                    limit.Commit(50);
                    await db.SaveChangesAsync();
                })
        );

        var final = await ReadAsync();
        Assert.Equal(8 * 50, final.UsedBytes);
        Assert.Equal(300, final.ReservedBytes);
    }
}
