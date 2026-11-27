using TaxVision.Auth.Application.CentralLogin;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Login central: una oficina donde la cuenta está asegurada no entra en el selector (I-2).</summary>
public sealed class CentralLoginAuthenticationTests
{
    [Fact]
    public async Task An_office_where_the_account_is_secured_is_left_out_of_the_selector()
    {
        var world = new AccountSessionFixture(UserActorType.TenantEmployee);

        var before = await AuthenticateAsync(world);
        world.Admin.Secure();
        var after = await AuthenticateAsync(world);

        Assert.Single(before);
        Assert.Empty(after);
        Assert.Equal(0, world.Admin.FailedLoginCount);
    }

    private static async Task<IReadOnlyList<AuthenticatedOffice>> AuthenticateAsync(AccountSessionFixture world)
    {
        var (offices, _) = await CentralLoginAuthentication.AuthenticateAsync(
            world.Admin.Email,
            "secret",
            [UserAccountKind.Staff],
            null,
            SessionSurface.Workspace,
            world.Users,
            world.Tenants,
            new PrefixedPasswordHasher(),
            new NoMfaRepository(),
            world.TokenService,
            false,
            CancellationToken.None
        );
        return offices;
    }
}
