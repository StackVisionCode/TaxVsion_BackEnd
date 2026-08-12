using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Sms.Application.Permissions.Consumers;
using TaxVision.Sms.Domain.Permissions;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

public sealed class PermissionsProjectionConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid RoleAdmin = Guid.NewGuid();
    private static readonly Guid RoleOther = Guid.NewGuid();

    [Fact]
    public async Task UserRolesChanged_creates_projection_with_sms_send()
    {
        var users = new FakeUserPermissionsProjectionRepository();
        var uow = new FakeUnitOfWork();
        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = Tenant,
            UserId = User,
            PermissionsVersion = 3,
            PermissionCodes = ["sms.send", "docs.read"],
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
        Assert.Contains("sms.send", stored!.PermissionCodes());
        Assert.Equal(3, stored.PermissionsVersion);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task UserRolesChanged_updates_existing_projection_when_newer()
    {
        var users = new FakeUserPermissionsProjectionRepository();
        users.Seed(UserPermissionsProjection.Create(Tenant, User, 1, ["docs.read"], [RoleAdmin]));
        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = Tenant,
            UserId = User,
            PermissionsVersion = 2,
            PermissionCodes = ["sms.send"],
            RoleIds = [RoleAdmin],
            ActorType = "TenantAdmin",
        };

        await UserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            users,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<UserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await users.GetAsync(Tenant, User);
        Assert.Contains("sms.send", stored!.PermissionCodes());
        Assert.DoesNotContain("docs.read", stored.PermissionCodes());
        Assert.Equal(2, stored.PermissionsVersion);
    }

    [Fact]
    public async Task RolePermissionsChanged_recomputes_union_for_multi_role_user()
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
            PermissionCodes = ["sms.send"],
            PermissionsVersion = 2,
        };

        await RolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roles,
            users,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            NullLogger<RolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await users.GetAsync(Tenant, User);
        // Union of the changed RoleAdmin (sms.send) and the untouched RoleOther (other.perm).
        Assert.Contains("sms.send", stored!.PermissionCodes());
        Assert.Contains("other.perm", stored.PermissionCodes());
        Assert.DoesNotContain("stale", stored.PermissionCodes());
    }
}
