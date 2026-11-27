using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Domain.Users;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>Las 10 reglas de 03 §6.4, en orden: la primera que falla decide.</summary>
public sealed class CredentialAdministrationPolicyTests
{
    private static readonly Guid Office = Guid.NewGuid();

    private readonly User _owner = Owner(Office);
    private readonly User _admin = Staff(Office, UserActorType.TenantAdmin);
    private readonly User _employee = Staff(Office, UserActorType.TenantEmployee, verified: true);

    private static Result Check(
        User actor,
        User target,
        bool ownerOnly = true,
        CredentialAdministrationAction action = CredentialAdministrationAction.SendResetLink,
        UserAccountKind expectedKind = UserAccountKind.Staff
    ) => CredentialAdministrationPolicy.CanActOn(actor, target, action, expectedKind, ownerOnly);

    [Fact]
    public void The_owner_can_act_on_a_verified_employee() => Assert.True(Check(_owner, _employee).IsSuccess);

    [Fact]
    public void Rule1_a_target_of_another_office_is_not_found() =>
        AssertError("User.NotFound", Check(_owner, Staff(Guid.NewGuid(), UserActorType.TenantEmployee, true)));

    [Fact]
    public void Rule2_a_portal_target_on_the_staff_endpoint_is_not_found() =>
        AssertError("User.NotFound", Check(_owner, Staff(Office, UserActorType.CustomerPortal, true)));

    [Fact]
    public void Rule2_a_staff_target_on_the_portal_endpoint_is_not_found() =>
        AssertError("User.NotFound", Check(_owner, _employee, expectedKind: UserAccountKind.Portal));

    [Fact]
    public void Rule3_an_employee_cannot_act() =>
        AssertError("Auth.CredentialAdmin.ActorNotAllowed", Check(_employee, _admin, ownerOnly: false));

    [Fact]
    public void Rule3_a_platform_admin_cannot_act()
    {
        var platformActor = Staff(PlatformTenant.Id, UserActorType.PlatformAdmin);
        var platformTarget = Staff(PlatformTenant.Id, UserActorType.PlatformAdmin, true);

        AssertError("Auth.CredentialAdmin.ActorNotAllowed", Check(platformActor, platformTarget, ownerOnly: false));
    }

    [Fact]
    public void Rule3_an_inactive_admin_cannot_act()
    {
        var admin = Staff(Office, UserActorType.TenantAdmin);
        Assert.True(admin.Deactivate(_owner.Id, DateTime.UtcNow).IsSuccess);

        AssertError("Auth.CredentialAdmin.ActorNotAllowed", Check(admin, _employee, ownerOnly: false));
    }

    [Fact]
    public void Rule4_with_owner_only_a_co_admin_cannot_act() =>
        AssertError("Auth.CredentialAdmin.OwnerOnly", Check(_admin, _employee));

    [Fact]
    public void Rule4_comes_before_the_self_rule() =>
        AssertError("Auth.CredentialAdmin.OwnerOnly", Check(_admin, _admin));

    [Fact]
    public void Rule5_nobody_acts_on_their_own_account() =>
        AssertError("Auth.CredentialAdmin.SelfNotAllowed", Check(_owner, _owner));

    [Fact]
    public void Rule6_the_owner_is_protected() =>
        AssertError("Auth.CredentialAdmin.TargetProtected", Check(_owner, Owner(Office)));

    [Fact]
    public void Rule6_a_co_admin_cannot_act_on_the_owner_even_when_admins_are_allowed() =>
        AssertError("Auth.CredentialAdmin.TargetProtected", Check(_admin, _owner, ownerOnly: false));

    [Fact]
    public void Rule7_a_platform_admin_target_is_not_found()
    {
        // Fila heredada: un PlatformAdmin que quedó dentro de una oficina.
        var misplaced = Staff(PlatformTenant.Id, UserActorType.PlatformAdmin, true);
        misplaced.SetTenant(Office);

        AssertError("User.NotFound", Check(_owner, misplaced));
    }

    [Fact]
    public void Rule8_only_the_owner_acts_on_another_admin() =>
        AssertError(
            "Auth.CredentialAdmin.Hierarchy",
            Check(_admin, Staff(Office, UserActorType.TenantAdmin, true), ownerOnly: false)
        );

    [Fact]
    public void Rule8_the_owner_can_act_on_another_admin() =>
        Assert.True(Check(_owner, Staff(Office, UserActorType.TenantAdmin, true)).IsSuccess);

    [Fact]
    public void Rule9_an_inactive_target_must_be_reactivated_first()
    {
        var employee = Staff(Office, UserActorType.TenantEmployee, true);
        employee.Deactivate(_owner.Id, DateTime.UtcNow);

        AssertError("Auth.CredentialAdmin.TargetInactive", Check(_owner, employee));
    }

    [Fact]
    public void Rule10_a_reset_link_needs_a_verified_email() =>
        AssertError("Auth.CredentialAdmin.EmailUnverified", Check(_owner, Staff(Office, UserActorType.TenantEmployee)));

    [Fact]
    public void Rule10_other_actions_do_not_need_a_verified_email() =>
        Assert.True(
            Check(
                _owner,
                Staff(Office, UserActorType.TenantEmployee),
                action: CredentialAdministrationAction.ResetMfa
            ).IsSuccess
        );

    private static void AssertError(string code, Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
    }

    private static User Owner(Guid tenantId) =>
        Verified(
            User.Register(
                tenantId,
                "Olga",
                "Owner",
                $"owner-{Guid.NewGuid():N}@example.com",
                "h",
                UserActorType.TenantAdmin,
                onboardingId: Guid.NewGuid()
            ).Value
        );

    private static User Staff(Guid tenantId, UserActorType actorType, bool verified = false)
    {
        var user = User.Register(
            tenantId,
            "Sam",
            "Staff",
            $"staff-{Guid.NewGuid():N}@example.com",
            "h",
            actorType,
            actorType == UserActorType.CustomerPortal ? Guid.NewGuid() : null
        ).Value;
        return verified ? Verified(user) : user;
    }

    private static User Verified(User user)
    {
        user.VerifyEmail();
        return user;
    }
}
