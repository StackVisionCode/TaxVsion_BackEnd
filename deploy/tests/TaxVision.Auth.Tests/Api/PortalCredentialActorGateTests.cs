using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using BuildingBlocks.Sessions;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Session;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Auth.Api.Security;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// SR-11: el gate generalizado de los M2M del portal (sesiones e invitación) exige el token del actor, su actor
/// type permitido, la oficina de la ruta, el permiso en vivo con Deny y audita cada rechazo con el actor type.
/// </summary>
public sealed class PortalCredentialActorGateTests
{
    private const string Issuer = "TaxVision.Auth";
    private const string ServicesAudience = "TaxVision.Services";
    private static readonly string Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static readonly ForwardedActorRequirement Sessions = new(
        [ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin],
        CustomersPermissions.View,
        RequireReauthentication: false
    );

    private static readonly ForwardedActorRequirement Invitation = new(
        [ActorType.TenantAdmin, ActorType.PlatformAdmin],
        CustomersPermissions.Manage,
        RequireReauthentication: false
    );

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakePermissions _permissions = new();
    private readonly FakeAuthAuditWriter _audit = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    public PortalCredentialActorGateTests()
    {
        _permissions.Codes.Add(CustomersPermissions.View);
        _permissions.Codes.Add(CustomersPermissions.Manage);
    }

    [Theory]
    [InlineData(ActorType.TenantEmployee)]
    [InlineData(ActorType.TenantAdmin)]
    public async Task Each_office_actor_type_passes_the_sessions_gate(ActorType actorType)
    {
        var result = await CheckAsync(Mint(actorType: actorType.ToString()), Sessions);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(actorType, result.Value.ActorType);
        Assert.Equal(_userId, result.Value.UserId);
        Assert.Empty(_audit.Logs);
    }

    [Fact]
    public async Task A_platform_admin_passes_with_its_platform_tenant_without_a_permission_snapshot()
    {
        _permissions.Codes.Clear();

        var result = await CheckAsync(
            Mint(actorType: "PlatformAdmin", tenantId: PlatformTenant.Id),
            Invitation,
            tenantId: PlatformTenant.Id
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(ActorType.PlatformAdmin, result.Value.ActorType);
        Assert.True(
            await Gate().HasPermissionAsync(result.Value, CustomersPermissions.PortalSessionsLocationView, default)
        );
    }

    [Fact]
    public async Task A_platform_admin_token_does_not_reach_an_office_tenant()
    {
        var result = await CheckAsync(Mint(actorType: "PlatformAdmin", tenantId: PlatformTenant.Id), Sessions);

        AssertInvalid(result);
        Assert.Equal("PlatformAdmin", Assert.Single(_audit.Logs).ActorType);
    }

    [Fact]
    public async Task An_employee_is_not_allowed_on_the_invitation() =>
        AssertInvalid(await CheckAsync(Mint(actorType: "TenantEmployee"), Invitation));

    [Theory]
    [InlineData("CustomerPortal")]
    [InlineData("Service")]
    [InlineData("0")]
    public async Task Other_actor_types_are_rejected(string actorType) =>
        AssertInvalid(await CheckAsync(Mint(actorType: actorType), Sessions));

    [Fact]
    public async Task A_deny_on_the_permission_is_forbidden_and_audited_with_the_actor_type()
    {
        _permissions.Codes.Remove(CustomersPermissions.Manage);

        var result = await CheckAsync(
            Mint(actorType: "TenantEmployee"),
            Sessions with
            {
                RequiredPermission = CustomersPermissions.Manage,
            }
        );

        Assert.Equal("Auth.CredentialAdmin.ActorNotAllowed", result.Error.Code);
        var log = Assert.Single(_audit.Logs);
        Assert.Equal("auth.credential_admin.denied", log.Action);
        Assert.Equal("TenantEmployee", log.ActorType);
        Assert.Equal(_userId, log.UserId);
        Assert.False(log.Success);
    }

    [Fact]
    public async Task The_location_permission_is_read_for_the_validated_actor()
    {
        var actor = (await CheckAsync(Mint(actorType: "TenantEmployee"), Sessions)).Value;

        Assert.False(await Gate().HasPermissionAsync(actor, CustomersPermissions.PortalSessionsLocationView, default));
        _permissions.Codes.Add(CustomersPermissions.PortalSessionsLocationView);
        Assert.True(await Gate().HasPermissionAsync(actor, CustomersPermissions.PortalSessionsLocationView, default));
    }

    [Fact]
    public async Task Without_the_header_the_call_is_rejected_and_audited()
    {
        AssertInvalid(await CheckAsync(token: null, Sessions));

        var log = Assert.Single(_audit.Logs);
        Assert.Equal("auth.credential_admin.denied", log.Action);
        Assert.Null(log.ActorType);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    // OBS-04: el requestedByUserId sin token verificado nunca es el actor; va aparte en los detalles.
    [Fact]
    public async Task Without_the_header_the_claimed_requester_is_not_the_actor()
    {
        AssertInvalid(await CheckAsync(token: null, Sessions));

        var log = Assert.Single(_audit.Logs);
        Assert.Null(log.UserId);
        Assert.Null(log.ActorType);
        Assert.Equal(_userId, Detail(log, "claimedRequestedByUserId")?.GetGuid());
    }

    [Fact]
    public async Task Without_the_header_the_calling_service_is_the_actor()
    {
        var serviceId = Guid.NewGuid();
        var caller = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", serviceId.ToString()), new("actor_type", "Service"), new("client_id", "customer")],
                "Bearer"
            )
        );

        AssertInvalid(await CheckAsync(token: null, Sessions, caller: caller));

        var log = Assert.Single(_audit.Logs);
        Assert.Equal(serviceId, log.UserId);
        Assert.Equal("Service", log.ActorType);
        Assert.Equal("customer", Detail(log, "serviceClientId")?.GetString());
        Assert.Equal(_userId, Detail(log, "claimedRequestedByUserId")?.GetGuid());
    }

    [Fact]
    public async Task A_forged_token_does_not_name_the_actor()
    {
        AssertInvalid(await CheckAsync("Bearer-not-a-jwt", Sessions));

        Assert.Null(Assert.Single(_audit.Logs).UserId);
    }

    [Fact]
    public async Task With_a_token_of_another_user_the_actor_is_the_token_subject()
    {
        var claimed = Guid.NewGuid();

        AssertInvalid(await CheckAsync(Mint(), Sessions, requestedByUserId: claimed));

        var log = Assert.Single(_audit.Logs);
        Assert.Equal(_userId, log.UserId);
        Assert.Equal("TenantAdmin", log.ActorType);
        Assert.Equal(claimed, Detail(log, "claimedRequestedByUserId")?.GetGuid());
    }

    [Theory]
    [InlineData("pwd_change", "required")]
    [InlineData("mfa_enroll", "pending")]
    public async Task A_restricted_token_is_rejected(string type, string value) =>
        AssertInvalid(await CheckAsync(Mint(extra: new Claim(type, value)), Sessions));

    [Fact]
    public async Task A_token_of_another_user_than_the_requester_is_rejected() =>
        AssertInvalid(await CheckAsync(Mint(), Sessions, requestedByUserId: Guid.NewGuid()));

    [Fact]
    public async Task Without_requester_the_actor_is_the_token_subject()
    {
        var result = await CheckAsync(Mint(), Sessions, requestedByUserId: null, omitRequester: true);

        Assert.Equal(_userId, result.Value.UserId);
    }

    private static JsonElement? Detail(AuthAuditLog log, string name) =>
        JsonDocument.Parse(log.DetailsJson!).RootElement.TryGetProperty(name, out var value)
        && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static void AssertInvalid(Result<ForwardedActor> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal("Auth.CredentialAdmin.ActorTokenInvalid", result.Error.Code);
    }

    private Task<Result<ForwardedActor>> CheckAsync(
        string? token,
        ForwardedActorRequirement requirement,
        Guid? tenantId = null,
        Guid? requestedByUserId = null,
        bool omitRequester = false,
        ClaimsPrincipal? caller = null
    )
    {
        var context = new DefaultHttpContext();
        if (caller is not null)
            context.User = caller;
        if (token is not null)
            context.Request.Headers[ForwardedActorToken.HeaderName] = $"Bearer {token}";
        return Gate()
            .CheckAsync(
                context.Request,
                tenantId ?? _tenantId,
                Guid.NewGuid(),
                omitRequester ? null : requestedByUserId ?? _userId,
                requirement,
                "list_sessions",
                null,
                null,
                CancellationToken.None
            );
    }

    private PortalCredentialActorGate Gate() =>
        new(
            new ForwardedActorTokenValidator(
                new StaticJwtBearerOptions(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Audience"] = ServicesAudience })
                    .Build(),
                new NoDenylist(),
                Options.Create(new SessionDenylistOptions()),
                _permissions,
                NullLogger<ForwardedActorTokenValidator>.Instance
            ),
            new InMemoryUserRepository(),
            _audit,
            _unitOfWork,
            new FixedCorrelation()
        );

    private string Mint(string actorType = "TenantAdmin", Guid? tenantId = null, Claim? extra = null)
    {
        var claims = new List<Claim>
        {
            new("sub", _userId.ToString()),
            new("tenant_id", (tenantId ?? _tenantId).ToString()),
            new("actor_type", actorType),
            new("sid", Guid.NewGuid().ToString()),
        };
        if (extra is not null)
            claims.Add(extra);
        var jwt = new JwtSecurityToken(
            Issuer,
            ServicesAudience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

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
                ClockSkew = TimeSpan.FromSeconds(30),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            },
        };

        public JwtBearerOptions CurrentValue => _options;

        public JwtBearerOptions Get(string? name) => _options;

        public IDisposable? OnChange(Action<JwtBearerOptions, string?> listener) => null;
    }

    private sealed class NoDenylist : ISessionDenylistReader
    {
        public Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class FakePermissions : IUserPermissionsProjectionReader
    {
        public List<string> Codes { get; } = [];

        public Task<UserPermissionsSnapshot?> GetSnapshotAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<UserPermissionsSnapshot?>(
                Codes.Count == 0 ? null : new UserPermissionsSnapshot(1, Codes.ToArray())
            );
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoScope();

        private sealed class NoScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
