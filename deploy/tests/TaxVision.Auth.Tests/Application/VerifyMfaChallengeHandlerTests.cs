using BuildingBlocks.Results;
using TaxVision.Auth.Application.Mfa.Commands;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Paso 2 del login clásico. La superficie la fija el controller: la web abre sesiones web y la ruta de las apps
/// abre sesiones móviles, solo para quien la política permite (el staff de oficina nunca).
/// </summary>
public sealed class VerifyMfaChallengeHandlerTests
{
    private const string Ticket = "login-ticket";
    private const string Code = "123456";

    private static (AccountSessionFixture World, InMemoryMfaRepository Mfa) WithChallenge(
        UserActorType actorType,
        SessionSurface askedOn = SessionSurface.Workspace
    )
    {
        var world = new AccountSessionFixture(actorType);
        var mfa = new InMemoryMfaRepository();
        mfa.Challenges.Add(
            MfaChallenge.Create(
                world.Tenant.Id,
                world.Admin.Id,
                Guid.NewGuid(),
                world.TokenService.Hash(Ticket),
                world.TokenService.Hash(Code),
                TimeSpan.FromMinutes(5),
                askedOn
            )
        );
        return (world, mfa);
    }

    private static Task<Result<MfaVerifyResponse>> VerifyAsync(
        AccountSessionFixture world,
        InMemoryMfaRepository mfa,
        SessionSurface surface
    ) =>
        VerifyMfaChallengeHandler.Handle(
            new VerifyMfaChallengeCommand(Ticket, Code, null, Surface: surface),
            mfa,
            world.TokenService,
            world.Users,
            world.Tenants,
            world.Roles,
            new NoTotp(),
            new NoProtector(),
            world.Issuer,
            world.Sessions,
            new NoopSessionTakeoverTicketStore(),
            world.Audit,
            world.Request,
            world.Correlation,
            world.UnitOfWork,
            CancellationToken.None
        );

    [Fact]
    public async Task A_secured_account_cannot_finish_the_mfa_step()
    {
        // La oficina aseguró la cuenta entre la contraseña y el código: el desafío pendiente ya no abre sesión.
        var (world, mfa) = WithChallenge(UserActorType.TenantAdmin);
        world.Admin.Secure();

        var result = await VerifyAsync(world, mfa, SessionSurface.Workspace);

        Assert.True(result.IsFailure);
        Assert.Empty(world.Sessions.Sessions);
    }

    [Fact]
    public async Task The_web_route_keeps_opening_web_sessions()
    {
        var (world, mfa) = WithChallenge(UserActorType.TenantAdmin);

        var result = await VerifyAsync(world, mfa, SessionSurface.Workspace);

        Assert.NotNull(result.Value.Tokens);
        Assert.Equal(SessionChannel.Web, Assert.Single(world.Sessions.Sessions).Channel);
    }

    [Fact]
    public async Task A_platform_admin_completing_mfa_on_the_support_app_gets_a_mobile_session()
    {
        var (world, mfa) = WithChallenge(UserActorType.PlatformAdmin, askedOn: SessionSurface.Mobile);

        var result = await VerifyAsync(world, mfa, SessionSurface.Mobile);

        Assert.NotNull(result.Value.Tokens);
        Assert.Equal(SessionChannel.Mobile, Assert.Single(world.Sessions.Sessions).Channel);
        Assert.Equal(SessionSurface.Mobile, Assert.Single(world.Sessions.Tokens).Surface);
    }

    // El desafío queda atado a la superficie que lo pidió, aunque la política deje a esa cuenta usar la otra.
    [Fact]
    public async Task A_challenge_asked_on_the_web_is_not_completed_on_the_app_route()
    {
        var (world, mfa) = WithChallenge(UserActorType.PlatformAdmin, askedOn: SessionSurface.Workspace);

        var result = await VerifyAsync(world, mfa, SessionSurface.Mobile);

        Assert.Equal("Auth.MfaInvalid", result.Error.Code);
        Assert.Empty(world.Sessions.Sessions);
        Assert.Null(mfa.Challenges[0].ConsumedAtUtc);
    }

    [Fact]
    public async Task A_challenge_asked_on_the_app_is_not_completed_on_the_web_route()
    {
        var (world, mfa) = WithChallenge(UserActorType.PlatformAdmin, askedOn: SessionSurface.Mobile);

        var result = await VerifyAsync(world, mfa, SessionSurface.Workspace);

        Assert.Equal("Auth.MfaInvalid", result.Error.Code);
        Assert.Empty(world.Sessions.Sessions);
        Assert.Null(mfa.Challenges[0].ConsumedAtUtc);
    }

    // La otra superficie no gasta los intentos ni el desafío: su dueño lo termina por donde lo pidió.
    [Fact]
    public async Task A_try_on_the_wrong_route_does_not_spend_the_challenge()
    {
        var (world, mfa) = WithChallenge(UserActorType.PlatformAdmin, askedOn: SessionSurface.Mobile);

        await VerifyAsync(world, mfa, SessionSurface.Workspace);
        var result = await VerifyAsync(world, mfa, SessionSurface.Mobile);

        Assert.Equal(0, mfa.Challenges[0].Attempts);
        Assert.NotNull(result.Value.Tokens);
        Assert.Equal(SessionChannel.Mobile, Assert.Single(world.Sessions.Sessions).Channel);
    }

    // Un desafío del login web no se puede terminar por la ruta de las apps para llevarse una sesión sin binding.
    [Fact]
    public async Task Office_staff_completing_mfa_on_the_app_route_get_no_session()
    {
        var (world, mfa) = WithChallenge(UserActorType.TenantAdmin);

        var result = await VerifyAsync(world, mfa, SessionSurface.Mobile);

        Assert.Equal("Auth.MfaInvalid", result.Error.Code);
        Assert.Empty(world.Sessions.Sessions);
    }

    private sealed class NoTotp : TaxVision.Auth.Application.Abstractions.ITotpService
    {
        public string GenerateSecret() => throw new NotSupportedException();

        public string BuildOtpAuthUri(string accountName, string base32Secret, string issuer) =>
            throw new NotSupportedException();

        public bool ValidateCode(string base32Secret, string code, DateTime utcNow) =>
            throw new NotSupportedException();
    }

    private sealed class NoProtector : BuildingBlocks.Security.ISecretProtector
    {
        public string Protect(string plaintext) => throw new NotSupportedException();

        public bool TryUnprotect(
            string? protectedValue,
            out string plaintext,
            out BuildingBlocks.Security.SecretUnprotectFailure failure
        ) => throw new NotSupportedException();
    }
}
