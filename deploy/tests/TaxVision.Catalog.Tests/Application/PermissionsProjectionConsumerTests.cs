using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Catalog.Application.Permissions.Consumers;
using TaxVision.Catalog.Domain.Permissions;
using TaxVision.Catalog.Tests.Fakes;

namespace TaxVision.Catalog.Tests.Application;

public sealed class PermissionsProjectionConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid RoleAdmin = Guid.NewGuid();
    private static readonly Guid RoleOther = Guid.NewGuid();

    [Fact]
    public async Task UserRolesChanged_creates_projection_with_catalog_perms()
    {
        var users = new FakeUserPermissionsProjectionRepository();
        var uow = new FakeUnitOfWork();
        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = Tenant,
            UserId = User,
            PermissionsVersion = 3,
            PermissionCodes = ["catalog.read", "catalog.write"],
            RoleIds = [RoleAdmin],
            ActorType = "TenantAdmin",
        };

        await UserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            users,
            uow,
            new FakeCorrelationContext(),
            NullLogger<UserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await users.GetAsync(Tenant, User);
        Assert.NotNull(stored);
        Assert.Contains("catalog.write", stored!.PermissionCodes());
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    /// <summary>
    /// Contrato nuevo: este consumer solo cachea rol → permisos. La unión del usuario la manda Auth
    /// por titular (<c>UserRolesChanged</c>) con los denies ya restados; recomponerla acá resucitaba
    /// un permiso denegado.
    /// </summary>
    public async Task RolePermissionsChanged_leaves_the_user_projection_untouched()
    {
        var users = new FakeUserPermissionsProjectionRepository();
        users.Seed(UserPermissionsProjection.Create(Tenant, User, 5, ["stale"], [RoleAdmin, RoleOther]));
        var roles = new FakeRolePermissionsProjectionRepository();
        roles.Seed(RolePermissionsProjection.Create(Tenant, RoleAdmin, "Tenant Admin", 1, ["stale"]));
        roles.Seed(RolePermissionsProjection.Create(Tenant, RoleOther, "Employee", 1, ["other.perm"]));

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = Tenant,
            RoleId = RoleAdmin,
            RoleName = "Tenant Admin",
            PermissionCodes = ["catalog.write"],
            PermissionsVersion = 2,
        };

        await RolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roles,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<RolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        var storedRole = await roles.GetAsync(Tenant, RoleAdmin);
        Assert.Equal(["catalog.write"], storedRole!.PermissionCodes());

        var stored = await users.GetAsync(Tenant, User);
        Assert.Equal(["stale"], stored!.PermissionCodes());
    }
}
