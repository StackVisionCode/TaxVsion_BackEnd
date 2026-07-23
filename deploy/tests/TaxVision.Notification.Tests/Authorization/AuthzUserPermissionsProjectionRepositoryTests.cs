using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Domain.Authorization;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;

namespace TaxVision.Notification.Tests.Authorization;

/// <summary>
/// Cubre <see cref="AuthzUserPermissionsProjectionRepository"/> contra un
/// <see cref="NotificationDbContext"/> real (EF Core InMemory, mismo patrón que
/// <c>Persistence/TenantIsolationTests.cs</c>) — en particular <c>GetSnapshotAsync</c>, el único
/// método que <c>ProjectionPermissionsSource</c> (BuildingBlocks.Web) invoca en el hot path de
/// autorización cuando <c>Authorization:PermissionsSource=Projection</c>.
/// </summary>
public sealed class AuthzUserPermissionsProjectionRepositoryTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static NotificationDbContext CreateContext(string databaseName, FakeTenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseInMemoryDatabase(databaseName).Options,
            tenantContext
        );

    [Fact]
    public async Task GetSnapshotAsync_returns_version_and_codes_for_an_active_user()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var projection = AuthzUserPermissionsProjection.Create(
                tenantId,
                userId,
                3,
                ["notification.template.view", "notification.log.view"],
                []
            );
            await seedDb.AuthzUserPermissionsProjections.AddAsync(projection);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var repository = new AuthzUserPermissionsProjectionRepository(db);

        var snapshot = await repository.GetSnapshotAsync(tenantId, userId);

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot!.PermissionsVersion);
        Assert.Contains("notification.template.view", snapshot.PermissionCodes);
        Assert.Contains("notification.log.view", snapshot.PermissionCodes);
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_null_when_no_projection_exists_for_the_user()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using var db = CreateContext(databaseName, tenantContext);
        var repository = new AuthzUserPermissionsProjectionRepository(db);

        var snapshot = await repository.GetSnapshotAsync(tenantId, Guid.NewGuid());

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task FindActiveByTenantAndRoleIdAsync_returns_only_users_with_that_role()
    {
        var databaseName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);

        await using (var seedDb = CreateContext(databaseName, tenantContext))
        {
            var withRole = AuthzUserPermissionsProjection.Create(tenantId, Guid.NewGuid(), 1, ["a"], [roleId]);
            var withoutRole = AuthzUserPermissionsProjection.Create(tenantId, Guid.NewGuid(), 1, ["b"], [otherRoleId]);
            await seedDb.AuthzUserPermissionsProjections.AddRangeAsync(withRole, withoutRole);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateContext(databaseName, tenantContext);
        var repository = new AuthzUserPermissionsProjectionRepository(db);

        var affected = await repository.FindActiveByTenantAndRoleIdAsync(tenantId, roleId);

        var user = Assert.Single(affected);
        Assert.Contains(roleId, user.RoleIds());
    }
}
