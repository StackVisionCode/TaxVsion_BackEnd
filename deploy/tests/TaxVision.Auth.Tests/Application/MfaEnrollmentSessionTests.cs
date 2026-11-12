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
/// Hallazgo del dueño (2026-11-11): con MFA obligatorio y sin configurar, el login entregaba una sesión completa y
/// solo el front mandaba a la pantalla de configuración; borrar la URL y recargar abría el dashboard. Ahora, mientras
/// falte configurar el MFA, toda emisión o renovación del espacio de trabajo sale como token solo-MFA.
/// </summary>
public sealed class MfaEnrollmentSessionTests
{
    private static (
        AuthSessionIssuer Issuer,
        CapturingJwtTokenGenerator Jwt,
        InMemoryMfaRepository Mfa,
        User User
    ) Build(UserActorType actorType = UserActorType.TenantEmployee, bool enforced = true)
    {
        var user = User.Register(Guid.NewGuid(), "Eve", "Staff", "eve@example.com", "hash", actorType).Value;
        var jwt = new CapturingJwtTokenGenerator();
        var mfa = new InMemoryMfaRepository();
        var issuer = new AuthSessionIssuer(
            new InMemorySessionRepository(),
            new PredictableTokenService(),
            jwt,
            new FakeRequestContext(),
            Options.Create(new RefreshTokenOptions()),
            mfa,
            Options.Create(new MfaOptions { Enforced = enforced })
        );
        return (issuer, jwt, mfa, user);
    }

    // La oficina exige MFA a empleados y clientes (Settings → Security).
    private static TenantMfaPolicy RequireForEveryone(Guid tenantId)
    {
        var policy = TenantMfaPolicy.CreateDefault(tenantId);
        policy.Update(requireForEmployees: true, requireForCustomerPortal: true, trustedDeviceDays: 30);
        return policy;
    }

    [Fact]
    public async Task A_user_who_must_enroll_only_gets_an_mfa_enrollment_token()
    {
        var (issuer, jwt, mfa, user) = Build();
        mfa.Policy = RequireForEveryone(user.TenantId);

        await issuer.StartSessionAsync(user, "UTC", [], ["pwd"], "Chrome", SessionSurface.Workspace);

        Assert.True(Assert.Single(jwt.Issued).MfaEnrollmentOnly);
    }

    [Fact]
    public async Task A_user_with_a_confirmed_method_gets_the_full_session()
    {
        var (issuer, jwt, mfa, user) = Build();
        mfa.Policy = RequireForEveryone(user.TenantId);
        mfa.Enroll(user);

        await issuer.StartSessionAsync(user, "UTC", [], ["pwd", "mfa"], "Chrome", SessionSurface.Workspace);

        Assert.False(Assert.Single(jwt.Issued).MfaEnrollmentOnly);
    }

    // Decisión del dueño: si la oficina exige MFA con la sesión abierta, el próximo refresh baja a solo-MFA.
    [Fact]
    public async Task Refreshing_re_evaluates_the_policy_and_after_enrolling_returns_the_full_session()
    {
        var (issuer, jwt, mfa, user) = Build();
        var started = await issuer.StartSessionAsync(user, "UTC", [], ["pwd"], "Chrome", SessionSurface.Workspace);
        var session = UserSession.Start(user.TenantId, user.Id, "Chrome", null, null);
        var chain = RefreshToken.Create(
            user.TenantId,
            user.Id,
            session.Id,
            "hash",
            DateTime.UtcNow.AddDays(1),
            SessionSurface.Workspace
        );
        Assert.False(jwt.Issued[^1].MfaEnrollmentOnly);

        mfa.Policy = RequireForEveryone(user.TenantId);
        await issuer.RotateAsync(chain, session, user, "UTC", [], ["refresh"]);
        Assert.True(jwt.Issued[^1].MfaEnrollmentOnly);

        mfa.Enroll(user);
        var next = RefreshToken.Create(
            user.TenantId,
            user.Id,
            session.Id,
            "hash2",
            DateTime.UtcNow.AddDays(1),
            SessionSurface.Workspace
        );
        await issuer.RotateAsync(next, session, user, "UTC", [], ["refresh"]);
        Assert.False(jwt.Issued[^1].MfaEnrollmentOnly);
        Assert.NotNull(started.AccessToken);
    }

    [Fact]
    public async Task With_mfa_not_enforced_nobody_gets_an_enrollment_token()
    {
        var (issuer, jwt, mfa, user) = Build(enforced: false);
        mfa.Policy = RequireForEveryone(user.TenantId);

        await issuer.StartSessionAsync(user, "UTC", [], ["pwd"], "Chrome", SessionSurface.Workspace);

        Assert.False(Assert.Single(jwt.Issued).MfaEnrollmentOnly);
    }

    [Fact]
    public void The_enrollment_token_has_its_own_audience_a_marker_and_a_short_life()
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

        var restricted = generator.Generate(user, "UTC", Guid.NewGuid(), [], ["pwd"], mfaEnrollmentOnly: true);
        var full = generator.Generate(user, "UTC", Guid.NewGuid(), [], ["pwd"]);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(restricted.Token);
        Assert.Equal([MfaEnrollmentToken.Audience], token.Audiences);
        Assert.Contains(
            token.Claims,
            claim => claim.Type == MfaEnrollmentToken.ClaimType && claim.Value == MfaEnrollmentToken.Pending
        );
        Assert.True(restricted.ExpiresInSeconds <= 600);
        Assert.DoesNotContain(
            new JwtSecurityTokenHandler().ReadJwtToken(full.Token).Claims,
            claim => claim.Type == MfaEnrollmentToken.ClaimType
        );
    }
}
