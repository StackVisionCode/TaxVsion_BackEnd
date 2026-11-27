using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Permissions;
using BuildingBlocks.Sessions;
using BuildingBlocks.Web.Session;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Auth.Api.Security;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// SR-03 y SR-05: el token del actor que reenvía Customer se valida con la firma, el emisor y la vida de Auth, solo
/// con la audiencia de servicios; actor, oficina, sesión, step-up y permiso salen del token, nunca del cuerpo.
/// </summary>
public sealed class ForwardedActorTokenValidatorTests
{
    private const string Issuer = "TaxVision.Auth";
    private const string ServicesAudience = "TaxVision.Services";
    private static readonly string Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly FakeDenylist _denylist = new();
    private readonly FakePermissions _permissions = new();

    public ForwardedActorTokenValidatorTests() => _permissions.Codes.Add(CustomersPermissions.PortalCredentialsSecure);

    [Fact]
    public async Task A_valid_token_yields_the_actor_and_its_reauthentication()
    {
        var reauthAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        var result = await ValidateAsync(Bearer(Mint(reauthAt: reauthAt)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(_userId, result.Value.UserId);
        Assert.Equal(_sessionId, result.Value.SessionId);
        Assert.Equal(
            reauthAt.ToUnixTimeSeconds(),
            new DateTimeOffset(result.Value.ReauthenticatedAtUtc!.Value).ToUnixTimeSeconds()
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer ")]
    [InlineData("Bearer not-a-jwt")]
    public async Task A_missing_or_malformed_header_is_rejected(string? header) =>
        AssertInvalid(await ValidateAsync(header));

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected() =>
        AssertInvalid(
            await ValidateAsync(Bearer(Mint(secret: Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)))))
        );

    [Fact]
    public async Task An_expired_token_is_rejected() =>
        AssertInvalid(await ValidateAsync(Bearer(Mint(expires: DateTime.UtcNow.AddMinutes(-5)))));

    [Theory]
    [InlineData("TaxVision.PasswordChange")]
    [InlineData("TaxVision.MfaEnrollment")]
    [InlineData("TaxVision.Account")]
    [InlineData("Somebody.Else")]
    public async Task A_token_for_another_audience_is_rejected(string audience) =>
        AssertInvalid(await ValidateAsync(Bearer(Mint(audience: audience))));

    [Theory]
    [InlineData("pwd_change", "required")]
    [InlineData("mfa_enroll", "pending")]
    public async Task A_restricted_token_is_rejected_even_with_the_services_audience(string type, string value) =>
        AssertInvalid(await ValidateAsync(Bearer(Mint(extra: new Claim(type, value)))));

    [Fact]
    public async Task A_token_of_another_user_is_rejected() =>
        AssertInvalid(await ValidateAsync(Bearer(Mint(userId: Guid.NewGuid()))));

    [Fact]
    public async Task A_token_of_another_office_is_rejected() =>
        AssertInvalid(await ValidateAsync(Bearer(Mint(tenantId: Guid.NewGuid()))));

    [Theory]
    [InlineData("Service")]
    [InlineData("TenantEmployee")]
    [InlineData("CustomerPortal")]
    public async Task A_token_of_another_actor_type_is_rejected(string actorType) =>
        AssertInvalid(await ValidateAsync(Bearer(Mint(actorType: actorType))));

    [Fact]
    public async Task A_token_without_session_is_rejected() =>
        AssertInvalid(await ValidateAsync(Bearer(Mint(withSession: false))));

    [Fact]
    public async Task A_revoked_session_is_rejected()
    {
        _denylist.Denied.Add(_sessionId);

        AssertInvalid(await ValidateAsync(Bearer(Mint())));
    }

    [Fact]
    public async Task Without_reauth_at_a_mutation_is_stale_but_a_read_passes()
    {
        var token = Bearer(Mint(withReauth: false));

        var mutation = await ValidateAsync(token);
        var read = await ValidateAsync(token, requireReauthentication: false);

        Assert.Equal("Auth.CredentialAdmin.StaleReauthentication", mutation.Error.Code);
        Assert.True(read.IsSuccess);
        Assert.Null(read.Value.ReauthenticatedAtUtc);
    }

    [Fact]
    public async Task An_old_reauthentication_is_stale() =>
        Assert.Equal(
            "Auth.CredentialAdmin.StaleReauthentication",
            (await ValidateAsync(Bearer(Mint(reauthAt: DateTimeOffset.UtcNow.AddMinutes(-11))))).Error.Code
        );

    [Fact]
    public async Task A_permission_removed_or_denied_is_not_allowed()
    {
        _permissions.Codes.Clear();

        var result = await ValidateAsync(Bearer(Mint()));

        Assert.Equal("Auth.CredentialAdmin.ActorNotAllowed", result.Error.Code);
    }

    private static void AssertInvalid(BuildingBlocks.Results.Result<ForwardedActor> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal("Auth.CredentialAdmin.ActorTokenInvalid", result.Error.Code);
    }

    private Task<BuildingBlocks.Results.Result<ForwardedActor>> ValidateAsync(
        string? header,
        bool requireReauthentication = true
    ) =>
        new ForwardedActorTokenValidator(
            new StaticJwtBearerOptions(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Audience"] = ServicesAudience })
                .Build(),
            _denylist,
            Options.Create(new SessionDenylistOptions()),
            _permissions,
            NullLogger<ForwardedActorTokenValidator>.Instance
        ).ValidateAsync(
            header,
            _tenantId,
            _userId,
            ActorType.TenantAdmin,
            CustomersPermissions.PortalCredentialsSecure,
            requireReauthentication,
            CancellationToken.None
        );

    private static string Bearer(string token) => $"Bearer {token}";

    private string Mint(
        Guid? userId = null,
        Guid? tenantId = null,
        string actorType = "TenantAdmin",
        string audience = ServicesAudience,
        string? secret = null,
        DateTime? expires = null,
        bool withSession = true,
        Claim? extra = null,
        bool withReauth = true,
        DateTimeOffset? reauthAt = null
    )
    {
        var claims = new List<Claim>
        {
            new("sub", (userId ?? _userId).ToString()),
            new("tenant_id", (tenantId ?? _tenantId).ToString()),
            new("actor_type", actorType),
        };
        if (withSession)
            claims.Add(new Claim("sid", _sessionId.ToString()));
        if (extra is not null)
            claims.Add(extra);
        // Por omisión, step-up de hace un minuto.
        if (withReauth)
            claims.Add(
                new Claim(
                    "reauth_at",
                    (reauthAt ?? DateTimeOffset.UtcNow.AddMinutes(-1)).ToUnixTimeSeconds().ToString()
                )
            );

        var expiry = expires ?? DateTime.UtcNow.AddMinutes(15);
        var jwt = new JwtSecurityToken(
            Issuer,
            audience,
            claims,
            notBefore: expiry.AddMinutes(-30),
            expires: expiry,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret ?? Secret)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    // Igual que AddTaxVisionJwtAuthentication: el JwtBearer de Auth acepta varias audiencias; el validador la estrecha.
    private sealed class StaticJwtBearerOptions : IOptionsMonitor<JwtBearerOptions>
    {
        private readonly JwtBearerOptions _options = new()
        {
            TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Issuer,
                ValidAudience = ServicesAudience,
                ValidAudiences =
                [
                    ServicesAudience,
                    "TaxVision.Account",
                    "TaxVision.MfaEnrollment",
                    "TaxVision.PasswordChange",
                ],
                ClockSkew = TimeSpan.FromSeconds(30),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            },
        };

        public JwtBearerOptions CurrentValue => _options;

        public JwtBearerOptions Get(string? name) => _options;

        public IDisposable? OnChange(Action<JwtBearerOptions, string?> listener) => null;
    }

    private sealed class FakeDenylist : ISessionDenylistReader
    {
        public HashSet<Guid> Denied { get; } = [];

        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(Denied.Contains(sessionId));
    }

    private sealed class FakePermissions : IUserPermissionsProjectionReader
    {
        public List<string> Codes { get; } = [];

        public Task<UserPermissionsSnapshot?> GetSnapshotAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<UserPermissionsSnapshot?>(new UserPermissionsSnapshot(1, Codes.ToArray()));
    }
}
