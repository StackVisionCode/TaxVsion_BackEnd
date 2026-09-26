using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>La misma persona es empleado y cliente de la oficina, con el mismo email y contraseñas distintas.</summary>
public sealed class LoginHandlerTests
{
    private const string Email = "ana@example.com";

    [Fact]
    public async Task The_portal_signs_in_to_the_portal_account()
    {
        var (world, _, portal) = DualAccounts();

        var result = await LoginAsync(world, "portal-pass", UserAccountKind.Portal);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserActorType.CustomerPortal.ToString(), result.Value.ActorType);
        Assert.Contains(world.Sessions.Sessions, session => session.UserId == portal.Id);
    }

    [Fact]
    public async Task The_workspace_signs_in_to_the_staff_account()
    {
        var (world, staff, _) = DualAccounts();

        var result = await LoginAsync(world, "staff-pass", UserAccountKind.Staff);

        Assert.True(result.IsSuccess);
        Assert.Contains(world.Sessions.Sessions, session => session.UserId == staff.Id);
    }

    [Fact]
    public async Task One_accounts_password_never_opens_the_other()
    {
        var (world, _, _) = DualAccounts();

        var result = await LoginAsync(world, "staff-pass", UserAccountKind.Portal);

        Assert.Equal("Auth.Invalid", result.Error.Code);
    }

    [Fact]
    public async Task Without_a_kind_the_staff_account_is_used_and_a_portal_only_email_still_signs_in()
    {
        var (world, staff, _) = DualAccounts();
        var portalOnly = new AccountSessionFixture();
        var client = User.Register(
            portalOnly.Tenant.Id,
            "Bo",
            "Client",
            "bo@example.com",
            "hashed:bo-pass",
            UserActorType.CustomerPortal,
            Guid.NewGuid()
        ).Value;
        portalOnly.Users.Add(client);

        var dual = await LoginAsync(world, "staff-pass", accountKind: null);
        var single = await LoginAsync(portalOnly, "bo-pass", accountKind: null, email: "bo@example.com");

        Assert.Contains(world.Sessions.Sessions, session => session.UserId == staff.Id);
        Assert.True(dual.IsSuccess && single.IsSuccess);
        Assert.Equal(UserActorType.CustomerPortal.ToString(), single.Value.ActorType);
    }

    private static (AccountSessionFixture World, User Staff, User Portal) DualAccounts()
    {
        var world = new AccountSessionFixture();
        var staff = User.Register(
            world.Tenant.Id,
            "Ana",
            "Staff",
            Email,
            "hashed:staff-pass",
            UserActorType.TenantEmployee
        ).Value;
        var portal = User.Register(
            world.Tenant.Id,
            "Ana",
            "Client",
            Email,
            "hashed:portal-pass",
            UserActorType.CustomerPortal,
            Guid.NewGuid()
        ).Value;
        world.Users.Add(staff);
        world.Users.Add(portal);
        return (world, staff, portal);
    }

    private static Task<Result<LoginResponse>> LoginAsync(
        AccountSessionFixture world,
        string password,
        UserAccountKind? accountKind,
        string email = Email
    ) =>
        LoginHandler.Handle(
            new LoginCommand(world.Tenant.Id, email, password, AccountKind: accountKind),
            world.Users,
            world.Tenants,
            new PrefixedPasswordHasher(),
            world.Roles,
            new NoMfaRepository(),
            world.Issuer,
            world.Sessions,
            new NoopSessionTakeoverTicketStore(),
            world.TokenService,
            new PermissiveLoginThrottler(),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            new FakeMessageBus(),
            Options.Create(new MfaOptions { Enforced = false }),
            CancellationToken.None
        );
}
