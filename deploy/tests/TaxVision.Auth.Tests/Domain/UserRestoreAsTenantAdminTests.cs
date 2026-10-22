using System.Reflection;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Domain.Users.Events;

namespace TaxVision.Auth.Tests.Domain;

public sealed class UserRestoreAsTenantAdminTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AdminId = Guid.NewGuid();

    /// <summary>
    /// Reproduce el cambio manual en la base: una TenantAdmin de una oficina con ActorType y Roles
    /// pasados a PlatformAdmin y sin prioridad de asiento. Register nunca permite crear este estado.
    /// </summary>
    internal static User MisplacedPlatformAdmin(Guid? onboardingId)
    {
        var user = User.Register(
            Guid.NewGuid(),
            "Carlos",
            "Castillo",
            "owner@office.test",
            "hash",
            UserActorType.TenantAdmin,
            onboardingId: onboardingId
        ).Value;
        typeof(User).GetProperty(nameof(User.ActorType))!.SetValue(user, UserActorType.PlatformAdmin);
        typeof(User).GetProperty(nameof(User.SlotPriorityAt))!.SetValue(user, null);
        var roles =
            (List<string>)
                typeof(User).GetField("_roles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(user)!;
        roles.Clear();
        roles.Add("PlatformAdmin");
        user.ClearDomainEvents();
        return user;
    }

    [Fact]
    public void Onboarding_account_comes_back_as_the_tenant_owner()
    {
        var user = MisplacedPlatformAdmin(onboardingId: Guid.NewGuid());

        var result = user.RestoreAsTenantAdmin(AdminId, Now);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(UserActorType.TenantAdmin, user.ActorType);
        Assert.True(user.IsOwner);
        Assert.Equal(["TenantAdmin"], user.Roles);
        var slot = Assert.IsType<UserSlotPriorityOverridden>(Assert.Single(user.DomainEvents));
        Assert.Null(slot.PreviousAt);
        Assert.Equal(DateTime.MinValue, slot.NewAt);
        Assert.Equal(AdminId, slot.ActorAdminId);
    }

    [Fact]
    public void Invited_account_comes_back_as_a_regular_tenant_admin()
    {
        var user = MisplacedPlatformAdmin(onboardingId: null);

        var result = user.RestoreAsTenantAdmin(AdminId, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserActorType.TenantAdmin, user.ActorType);
        Assert.False(user.IsOwner);
        Assert.Equal(Now, user.SlotPriorityAt);
    }

    [Fact]
    public void A_real_platform_admin_is_left_alone()
    {
        var user = User.Register(
            PlatformTenant.Id,
            "Jorge",
            "Turbi",
            "ops@platform.test",
            "hash",
            UserActorType.PlatformAdmin
        ).Value;

        var result = user.RestoreAsTenantAdmin(AdminId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("User.NotMisplacedPlatformAdmin", result.Error.Code);
        Assert.Equal(UserActorType.PlatformAdmin, user.ActorType);
    }

    [Theory]
    [InlineData(UserActorType.TenantAdmin)]
    [InlineData(UserActorType.TenantEmployee)]
    public void Accounts_that_are_not_platform_admins_are_not_touched(UserActorType actorType)
    {
        var user = User.Register(Guid.NewGuid(), "Ada", "Lovelace", "ada@office.test", "hash", actorType).Value;
        var slotBefore = user.SlotPriorityAt;

        var result = user.RestoreAsTenantAdmin(AdminId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(actorType, user.ActorType);
        Assert.Equal(slotBefore, user.SlotPriorityAt);
    }

    [Fact]
    public void The_acting_administrator_is_required()
    {
        var user = MisplacedPlatformAdmin(onboardingId: Guid.NewGuid());

        var result = user.RestoreAsTenantAdmin(Guid.Empty, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(UserActorType.PlatformAdmin, user.ActorType);
    }
}
