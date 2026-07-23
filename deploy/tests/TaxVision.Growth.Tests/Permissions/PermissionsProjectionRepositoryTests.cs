using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Growth.Infrastructure.Persistence;
using TaxVision.Growth.Infrastructure.Persistence.Permissions;
using TaxVision.Growth.Infrastructure.Persistence.Permissions.Repositories;

namespace TaxVision.Growth.Tests.Permissions;

/// <summary>
/// Cubre el repositorio EF real (no un fake) contra el proveedor InMemory — en particular que
/// <see cref="UserPermissionsProjectionRepository"/> satisface el puerto angosto compartido
/// (<c>IUserPermissionsProjectionReader.GetSnapshotAsync</c>, el que consulta
/// <c>ProjectionPermissionsSource</c>) además del puerto local rico, y que el filtro fail-closed
/// por tenant de <see cref="GrowthDbContext"/> aplica también a estas dos tablas nuevas.
/// </summary>
public sealed class PermissionsProjectionRepositoryTests
{
    [Fact]
    public async Task GetSnapshotAsync_returns_null_when_no_active_projection_exists()
    {
        await using var db = CreateContext(Guid.NewGuid());
        var repository = new UserPermissionsProjectionRepository(db);

        var snapshot = await repository.GetSnapshotAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_version_and_codes_for_the_active_projection()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var options = Options();

        await using (var seed = CreateContext(tenantId, options))
        {
            seed.UserPermissionsProjections.Add(
                UserPermissionsProjection.Create(tenantId, userId, 3, ["growth.codes.manage"], [])
            );
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(tenantId, options);
        var repository = new UserPermissionsProjectionRepository(db);

        var snapshot = await repository.GetSnapshotAsync(tenantId, userId);

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot!.PermissionsVersion);
        Assert.Equal(["growth.codes.manage"], snapshot.PermissionCodes);
    }

    [Fact]
    public async Task Global_tenant_filter_never_leaks_another_tenants_projection()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var options = Options();

        await using (var seed = CreateContext(tenantA, options))
        {
            seed.UserPermissionsProjections.Add(UserPermissionsProjection.Create(tenantA, userId, 1, ["a"], []));
            await seed.SaveChangesAsync();
        }

        await using var contextB = CreateContext(tenantB, options);
        var visible = await contextB.UserPermissionsProjections.ToListAsync();

        Assert.Empty(visible);
    }

    private static DbContextOptions<GrowthDbContext> Options() =>
        new DbContextOptionsBuilder<GrowthDbContext>()
            .UseInMemoryDatabase($"growth-perm-tests-{Guid.NewGuid():N}")
            .Options;

    private static GrowthDbContext CreateContext(Guid tenantId, DbContextOptions<GrowthDbContext>? options = null) =>
        new(options ?? Options(), new TestTenantContext(tenantId));

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }
}
