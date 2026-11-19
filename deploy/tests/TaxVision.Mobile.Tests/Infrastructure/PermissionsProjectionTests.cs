using BuildingBlocks.Permissions;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Domain.Projections;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;

namespace TaxVision.Mobile.Tests.Infrastructure;

public sealed class PermissionsProjectionTests
{
    [Fact]
    public async Task Versioned_permissions_and_lifecycle_stay_isolated_and_fail_closed()
    {
        using var db = new MobileDbContext(
            new DbContextOptionsBuilder<MobileDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        var repository = new MobileProjectionRepository(db);
        var t = Guid.NewGuid();
        var u = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Assert.Null(await repository.GetSnapshotAsync(t, u));
        var row = UserPermissionsProjection.Create(t, u, 3, ["read", "read"], [], now);
        repository.AddUserPermissions(row);
        await db.SaveChangesAsync();
        row.ApplyIfNewer(2, ["manage"], [], now);
        await db.SaveChangesAsync();
        Assert.Equal(["read"], (await repository.GetSnapshotAsync(t, u))!.PermissionCodes);
        row.ApplyIfNewer(3, ["read", "manage"], [], now);
        await db.SaveChangesAsync();
        Assert.Equal(2, (await repository.GetSnapshotAsync(t, u))!.PermissionCodes.Count);
        Assert.Null(await repository.GetSnapshotAsync(Guid.NewGuid(), u));
        row.SetActive(false, now.AddMinutes(1));
        row.SetActive(true, now);
        await db.SaveChangesAsync();
        Assert.Null(await repository.GetSnapshotAsync(t, u));
        await repository.PersistSnapshotAsync(t, u, new RemotePermissionsSnapshot(4, ["read"], []));
        Assert.Null(await repository.GetSnapshotAsync(t, u));
    }

    [Fact]
    public async Task Pull_snapshot_seeds_and_refreshes_missing_permissions()
    {
        using var db = new MobileDbContext(
            new DbContextOptionsBuilder<MobileDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        var repository = new MobileProjectionRepository(db);
        var t = Guid.NewGuid();
        var u = Guid.NewGuid();
        await repository.PersistSnapshotAsync(t, u, new RemotePermissionsSnapshot(1, ["read"], []));
        Assert.Equal(1, (await repository.GetSnapshotAsync(t, u))!.PermissionsVersion);
        await repository.PersistSnapshotAsync(t, u, new RemotePermissionsSnapshot(2, ["manage"], []));
        Assert.Equal(["manage"], (await repository.GetSnapshotAsync(t, u))!.PermissionCodes);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
