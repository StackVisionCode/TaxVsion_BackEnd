using System.IdentityModel.Tokens.Jwt;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Cambio obligatorio (03 §9.2): con <c>ChangeRequired</c>, toda emisión o renovación del espacio de trabajo y de la
/// app sale como token restringido. Si además falta enrolar el MFA exigido, primero la contraseña (D-12).
/// </summary>
public sealed class PasswordChangeSessionTests
{
    private static (
        AuthSessionIssuer Issuer,
        CapturingJwtTokenGenerator Jwt,
        InMemoryMfaRepository Mfa,
        User User
    ) Build(UserActorType actorType = UserActorType.TenantEmployee)
    {
        var customerId = actorType == UserActorType.CustomerPortal ? Guid.NewGuid() : (Guid?)null;
        var user = User.Register(
            Guid.NewGuid(),
            "Eve",
            "Staff",
            "eve@example.com",
            "hash",
            actorType,
            customerId
        ).Value;
        var jwt = new CapturingJwtTokenGenerator();
        var mfa = new InMemoryMfaRepository();
        var issuer = new AuthSessionIssuer(
            new InMemorySessionRepository(),
            new PredictableTokenService(),
            jwt,
            new FakeRequestContext(),
            Options.Create(new RefreshTokenOptions()),
            mfa,
            Options.Create(new MfaOptions { Enforced = true }),
            new NoSessionLocationResolver()
        );
        return (issuer, jwt, mfa, user);
    }

    [Theory]
    [InlineData(UserActorType.TenantEmployee, SessionSurface.Workspace)]
    [InlineData(UserActorType.CustomerPortal, SessionSurface.Workspace)]
    [InlineData(UserActorType.CustomerPortal, SessionSurface.Mobile)]
    public async Task A_required_change_only_gets_a_password_change_token(UserActorType actor, SessionSurface surface)
    {
        var (issuer, jwt, _, user) = Build(actor);
        user.RequireChange();

        await issuer.StartSessionAsync(user, "UTC", [], ["pwd"], "Chrome", surface);

        Assert.True(Assert.Single(jwt.PasswordChangeOnly));
        Assert.False(Assert.Single(jwt.Issued).MfaEnrollmentOnly);
    }

    // D-12: contraseña antes que MFA; tras cambiarla, el refresh baja a solo-MFA hasta enrolar.
    [Fact]
    public async Task With_both_pending_the_password_comes_first_and_then_the_mfa_enrollment()
    {
        var (issuer, jwt, mfa, user) = Build();
        var policy = TenantMfaPolicy.CreateDefault(user.TenantId);
        policy.Update(requireForEmployees: true, requireForCustomerPortal: true, trustedDeviceDays: 30);
        mfa.Policy = policy;
        user.RequireChange();

        await issuer.StartSessionAsync(user, "UTC", [], ["pwd"], "Chrome", SessionSurface.Workspace);
        Assert.True(jwt.PasswordChangeOnly[^1]);
        Assert.False(jwt.Issued[^1].MfaEnrollmentOnly);

        user.ChangePassword("new-hash", DateTime.UtcNow);
        await issuer.RotateAsync(Chain(user), Session(user), user, "UTC", [], ["refresh"]);
        Assert.False(jwt.PasswordChangeOnly[^1]);
        Assert.True(jwt.Issued[^1].MfaEnrollmentOnly);
    }

    [Fact]
    public async Task After_changing_the_password_the_refresh_returns_the_full_session()
    {
        var (issuer, jwt, _, user) = Build();
        user.RequireChange();
        await issuer.RotateAsync(Chain(user), Session(user), user, "UTC", [], ["refresh"]);
        Assert.True(jwt.PasswordChangeOnly[^1]);

        user.ChangePassword("new-hash", DateTime.UtcNow);
        await issuer.RotateAsync(Chain(user), Session(user), user, "UTC", [], ["refresh"]);

        Assert.False(jwt.PasswordChangeOnly[^1]);
        Assert.False(jwt.Issued[^1].MfaEnrollmentOnly);
    }

    [Fact]
    public void The_password_change_token_has_its_own_audience_a_marker_and_a_short_life()
    {
        var generator = new JwtTokenGenerator(
            Options.Create(
                new JwtOptions
                {
                    Secret = "test-secret-test-secret-test-secret-123456",
                    Issuer = "TaxVision.Auth",
                    Audience = "TaxVision.Services",
                    AccessMinutes = 15,
                }
            ),
            new SigningKeyProvider(
                Options.Create(new JwtOptions { Secret = "test-secret-test-secret-test-secret-123456" }),
                null!
            )
        );
        var user = User.Register(
            Guid.NewGuid(),
            "Eve",
            "Staff",
            "eve@example.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;

        var restricted = generator.Generate(user, "UTC", Guid.NewGuid(), [], ["pwd"], passwordChangeOnly: true);
        var full = generator.Generate(user, "UTC", Guid.NewGuid(), [], ["pwd"]);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(restricted.Token);
        Assert.Equal([PasswordChangeToken.Audience], token.Audiences);
        Assert.Contains(token.Claims, c => c.Type == "pwd_change" && c.Value == "required");
        Assert.True(restricted.ExpiresInSeconds <= 600);
        Assert.DoesNotContain(
            new JwtSecurityTokenHandler().ReadJwtToken(full.Token).Claims,
            claim => claim.Type == PasswordChangeToken.ClaimType
        );
    }

    // D-13: el front pide 12 o 15 caracteres según el MFA de la cuenta.
    [Theory]
    [InlineData(false, "false")]
    [InlineData(true, "true")]
    public void The_password_change_token_tells_whether_the_account_has_mfa(bool mfaEnabled, string expected)
    {
        var generator = new JwtTokenGenerator(
            Options.Create(
                new JwtOptions
                {
                    Secret = "test-secret-test-secret-test-secret-123456",
                    Issuer = "TaxVision.Auth",
                    Audience = "TaxVision.Services",
                }
            ),
            new SigningKeyProvider(
                Options.Create(new JwtOptions { Secret = "test-secret-test-secret-test-secret-123456" }),
                null!
            )
        );
        var user = User.Register(
            Guid.NewGuid(),
            "Eve",
            "Staff",
            "eve@example.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;
        if (mfaEnabled)
            user.EnableMfa();

        var restricted = generator.Generate(user, "UTC", Guid.NewGuid(), [], ["pwd"], passwordChangeOnly: true);
        var full = generator.Generate(user, "UTC", Guid.NewGuid(), [], ["pwd"]);

        Assert.Contains(
            new JwtSecurityTokenHandler().ReadJwtToken(restricted.Token).Claims,
            claim => claim.Type == "pwd_mfa" && claim.Value == expected
        );
        Assert.DoesNotContain(
            new JwtSecurityTokenHandler().ReadJwtToken(full.Token).Claims,
            claim => claim.Type == "pwd_mfa"
        );
    }

    private static UserSession Session(User user) => UserSession.Start(user.TenantId, user.Id, "Chrome", null, null);

    private static RefreshToken Chain(User user) =>
        RefreshToken.Create(
            user.TenantId,
            user.Id,
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            DateTime.UtcNow.AddDays(1),
            SessionSurface.Workspace
        );
}
