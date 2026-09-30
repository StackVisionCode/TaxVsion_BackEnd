using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Permissions;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

namespace TaxVision.CloudStorage.Tests.Authorization;

/// <summary>
/// <c>GetSnapshotAsync</c> es lo único que <c>ProjectionPermissionsSource</c> consulta para autorizar, y
/// corre en scopes donde el <c>ITenantContext</c> ambiente puede llegar vacío. Con el filtro global
/// fail-closed activo eso devolvería 0 filas y el usuario vería un 403 sin permiso faltante, así que el
/// reader tiene que apoyarse en el tenantId de la firma (viene del JWT) y no en el ambiente.
/// </summary>
public sealed class UserPermissionsProjectionRepositoryTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static CloudStorageDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    [Fact]
    public async Task GetSnapshotAsync_resolves_the_user_without_an_ambient_tenant()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var seeded = new FakeTenantContext();
        seeded.SetTenant(tenantId);
        await using (var seedDb = CreateContext(databaseName, seeded))
        {
            seedDb.UserPermissionsProjections.Add(
                UserPermissionsProjection.Create(
                    tenantId,
                    userId,
                    4,
                    ["cloudstorage.settings.manage", "cloudstorage.file.view"],
                    []
                )
            );
            await seedDb.SaveChangesAsync();
        }

        // Sin tenant ambiente: el filtro global compara contra Guid.Empty.
        await using var db = CreateContext(databaseName, new FakeTenantContext());
        var snapshot = await new UserPermissionsProjectionRepository(db).GetSnapshotAsync(tenantId, userId);

        Assert.NotNull(snapshot);
        Assert.Equal(4, snapshot.PermissionsVersion);
        Assert.Contains("cloudstorage.settings.manage", snapshot.PermissionCodes);
    }

    [Fact]
    public async Task GetSnapshotAsync_does_not_cross_tenants()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var seeded = new FakeTenantContext();
        seeded.SetTenant(tenantId);
        await using (var seedDb = CreateContext(databaseName, seeded))
        {
            seedDb.UserPermissionsProjections.Add(
                UserPermissionsProjection.Create(tenantId, userId, 1, ["cloudstorage.file.view"], [])
            );
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, new FakeTenantContext());
        var snapshot = await new UserPermissionsProjectionRepository(db).GetSnapshotAsync(Guid.NewGuid(), userId);

        Assert.Null(snapshot);
    }
}
