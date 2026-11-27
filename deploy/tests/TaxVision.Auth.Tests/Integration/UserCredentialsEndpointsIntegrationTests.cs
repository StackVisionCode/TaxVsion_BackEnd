using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Host real de Auth sobre una base temporal (03 §14.1, §14.3): step-up, tipo de actor, política y auditoría de los
/// endpoints de credenciales. Los permisos se conceden todos: aquí se prueba lo que viene después del RBAC.
/// </summary>
public sealed class UserCredentialsEndpointsIntegrationTests : IAsyncLifetime
{
    private AuthTestDatabase _database = null!;
    private CredentialsApiFactory _factory = null!;
    private Tenant _tenant = null!;
    private User _owner = null!;
    private User _coAdmin = null!;
    private User _employee = null!;
    private User _portal = null!;
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _serviceId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        _tenant = Tenant
            .Register(Guid.NewGuid(), $"t{Guid.NewGuid():N}"[..20], "Office", TenantKind.Customer, "America/New_York")
            .Value;
        _owner = NewUser(UserActorType.TenantAdmin, onboardingId: Guid.NewGuid());
        _coAdmin = NewUser(UserActorType.TenantAdmin);
        _employee = NewUser(UserActorType.TenantEmployee);
        _portal = NewUser(UserActorType.CustomerPortal, customerId: _customerId);

        // SR-05: Auth relee el permiso efectivo del actor; el Owner tiene el rol de sistema con los de credenciales.
        var adminRole = Role.Create(_tenant.Id, Role.SystemTenantAdmin, null, isSystem: true).Value;
        adminRole.SetPermissions(
            [
                PermissionCatalog.IdOf(PermissionCatalog.CustomersPortalCredentialsSendReset),
                PermissionCatalog.IdOf(PermissionCatalog.CustomersPortalCredentialsRequireChange),
                PermissionCatalog.IdOf(PermissionCatalog.CustomersPortalCredentialsSecure),
                PermissionCatalog.IdOf(PermissionCatalog.CustomersPortalMfaReset),
            ],
            seeding: true
        );

        await using var db = _database.NewContext(_tenant.Id);
        db.Add(_tenant);
        db.Users.AddRange(_owner, _coAdmin, _employee, _portal);
        db.Roles.Add(adminRole);
        db.UserRoles.Add(UserRole.Create(_owner.Id, adminRole.Id));
        await db.SaveChangesAsync();

        _factory = new CredentialsApiFactory(_database.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task A_mutation_without_recent_sign_in_answers_401()
    {
        var response = await Client(Token(_owner, "TenantAdmin", reauth: false))
            .PostAsync($"/auth/users/{_employee.Id}/credentials/reset-link", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Auth.ReauthenticationRequired", await CodeAsync(response));
    }

    [Fact]
    public async Task An_employee_token_is_forbidden()
    {
        var response = await Client(Token(_employee, "TenantEmployee"))
            .GetAsync($"/auth/users/{_coAdmin.Id}/credentials");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_platform_admin_does_not_reach_an_office_account()
    {
        var response = await Client(Token(Guid.NewGuid(), PlatformTenant.Id, "PlatformAdmin"))
            .PostAsync($"/auth/users/{_employee.Id}/credentials/reset-link", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("User.NotFound", await CodeAsync(response));
    }

    [Fact]
    public async Task A_co_admin_is_denied_and_the_denial_is_audited()
    {
        var response = await Client(Token(_coAdmin, "TenantAdmin"))
            .PostAsync($"/auth/users/{_employee.Id}/credentials/reset-link", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.OwnerOnly", await CodeAsync(response));
        await using var read = _database.NewContext(_tenant.Id);
        Assert.True(
            await read.AuthAuditLogs.AnyAsync(l =>
                l.Action == "auth.credential_admin.denied" && l.TargetId == _employee.Id && !l.Success
            )
        );
    }

    [Fact]
    public async Task The_owner_sends_reads_cancels_and_a_used_link_cannot_be_cancelled()
    {
        var client = Client(Token(_owner, "TenantAdmin"));
        var route = $"/auth/users/{_employee.Id}/credentials";

        var sent = await client.PostAsync($"{route}/reset-link", null);
        Assert.True(sent.StatusCode == HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var issued = await sent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Issued", issued.GetProperty("outcome").GetString());
        Assert.DoesNotContain("token\"", (await sent.Content.ReadAsStringAsync()).Replace("resetTokenId", ""));

        var status = await client.GetFromJsonAsync<JsonElement>(route);
        Assert.Equal("Pending", status.GetProperty("latestResetLink").GetProperty("status").GetString());
        Assert.Equal("Administrative", status.GetProperty("latestResetLink").GetProperty("origin").GetString());
        Assert.True(status.GetProperty("allowedActions").GetProperty("sendResetLink").GetBoolean());

        var cancelled = await client.DeleteAsync($"{route}/reset-link");
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        // Nuevo link y el afectado lo canjea antes de que la oficina lo cancele.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"{route}/reset-link", null)).StatusCode);
        await using (var db = _database.NewContext(_tenant.Id))
        {
            var pending = await db
                .PasswordResetTokens.Where(t => t.UserId == _employee.Id && t.RevokedAtUtc == null)
                .SingleAsync();
            pending.MarkUsed();
            await db.SaveChangesAsync();
        }

        var late = await client.DeleteAsync($"{route}/reset-link");
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.LinkAlreadyUsed", await CodeAsync(late));
    }

    [Fact]
    public async Task The_daily_cap_per_target_answers_429_with_audit()
    {
        await using (var db = _database.NewContext(_tenant.Id))
        {
            for (var i = 0; i < 5; i++)
                db.PasswordResetTokens.Add(
                    PasswordResetToken.Create(
                        _tenant.Id,
                        _employee.Id,
                        $"{Guid.NewGuid():N}",
                        null,
                        TimeSpan.FromHours(24),
                        ResetOrigin.Administrative,
                        _coAdmin.Id
                    )
                );
            await db.SaveChangesAsync();
        }

        var response = await Client(Token(_owner, "TenantAdmin"))
            .PostAsync($"/auth/users/{_employee.Id}/credentials/reset-link", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.TargetRateLimited", await CodeAsync(response));
        await using var read = _database.NewContext(_tenant.Id);
        Assert.True(await read.AuthAuditLogs.AnyAsync(l => l.Action == "auth.credential_admin.denied"));
    }

    [Fact]
    public async Task The_internal_portal_endpoint_rejects_a_stale_reauthentication()
    {
        var route = $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-credentials/reset-link";

        // La ventana sale del token reenviado (SR-03), no del cuerpo.
        var stale = await PortalClient(ActorToken(_owner, TimeSpan.FromMinutes(11)))
            .PostAsJsonAsync(
                route,
                new
                {
                    requestedByUserId = _owner.Id,
                    reauthenticatedAtUtc = DateTime.UtcNow,
                    actorIpAddress = "203.0.113.7",
                }
            );
        var fresh = await PortalClient(ActorToken(_owner))
            .PostAsJsonAsync(route, new { requestedByUserId = _owner.Id });

        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.StaleReauthentication", await CodeAsync(stale));
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    // SR-03: sin el token del actor, el sobre del servicio no alcanza aunque afirme un step-up reciente.
    [Theory]
    [InlineData("portal-credentials/reset-link")]
    [InlineData("portal-credentials/secure")]
    [InlineData("portal-mfa/reset")]
    public async Task A_service_call_without_the_actor_token_is_rejected_and_audited(string action)
    {
        var response = await PortalClient(actorToken: null)
            .PostAsJsonAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/{action}",
                new
                {
                    requestedByUserId = _owner.Id,
                    reauthenticatedAtUtc = DateTime.UtcNow,
                    reasonCode = "SuspectedCompromise",
                }
            );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorTokenInvalid", await CodeAsync(response));
        await using var read = _database.NewContext(_tenant.Id);
        var denied = await read.AuthAuditLogs.SingleAsync(l =>
            l.Action == "auth.credential_admin.denied" && l.TargetId == _portal.Id && !l.Success
        );
        // OBS-04: el Owner declarado no queda como actor sin su token.
        Assert.Equal(_serviceId, denied.UserId);
        Assert.Equal("Service", denied.ActorType);
        Assert.Equal(
            _owner.Id,
            JsonDocument.Parse(denied.DetailsJson!).RootElement.GetProperty("claimedRequestedByUserId").GetGuid()
        );
        Assert.Equal(CredentialStatus.Active, (await read.Users.SingleAsync(u => u.Id == _portal.Id)).CredentialStatus);
    }

    [Fact]
    public async Task The_actor_token_must_belong_to_the_requester()
    {
        var response = await PortalClient(ActorToken(_coAdmin))
            .PostAsJsonAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-credentials/reset-link",
                new { requestedByUserId = _owner.Id, reauthenticatedAtUtc = DateTime.UtcNow }
            );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorTokenInvalid", await CodeAsync(response));
    }

    // SR-05: un Deny sobre el permiso lo aplica también Auth, no solo Customer.
    [Fact]
    public async Task A_deny_on_the_permission_is_enforced_by_auth()
    {
        await using (var db = _database.NewContext(_tenant.Id))
        {
            db.UserPermissionDenies.Add(
                UserPermissionDeny.Create(
                    _owner.Id,
                    PermissionCatalog.IdOf(PermissionCatalog.CustomersPortalCredentialsSecure)
                )
            );
            await db.SaveChangesAsync();
        }

        var response = await PortalClient(ActorToken(_owner))
            .PostAsJsonAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-credentials/secure",
                new { requestedByUserId = _owner.Id, reasonCode = "SuspectedCompromise" }
            );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorNotAllowed", await CodeAsync(response));
    }

    // F4-L8: secure sobre un empleado; luego require change es una transición inválida (03 §4.6).
    [Fact]
    public async Task The_owner_secures_an_employee_and_require_change_is_then_an_invalid_transition()
    {
        var client = Client(Token(_owner, "TenantAdmin"));
        var route = $"/auth/users/{_employee.Id}/credentials";

        var secured = await client.PostAsJsonAsync(
            $"{route}/secure",
            new
            {
                reasonCode = "SuspectedCompromise",
                reasonNote = "private",
                identityConfirmed = true,
            }
        );
        Assert.True(secured.StatusCode == HttpStatusCode.OK, await secured.Content.ReadAsStringAsync());
        var body = await secured.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("alreadyInState").GetBoolean());

        var change = await client.PostAsJsonAsync($"{route}/require-change", new { reasonCode = "OfficePolicy" });
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.InvalidTransition", await CodeAsync(change));

        var status = await client.GetFromJsonAsync<JsonElement>(route);
        Assert.Equal("ResetRequired", status.GetProperty("credentialStatus").GetString());
        Assert.Equal("SuspectedCompromise", status.GetProperty("reasonCode").GetString());
        Assert.Equal("Incident", status.GetProperty("latestResetLink").GetProperty("origin").GetString());
        Assert.False(status.GetProperty("allowedActions").GetProperty("sendResetLink").GetBoolean());
        Assert.True(status.GetProperty("allowedActions").GetProperty("secure").GetBoolean());

        await using var read = _database.NewContext(_tenant.Id);
        var log = await read.AuthAuditLogs.SingleAsync(l => l.Action == "auth.user.secured");
        Assert.DoesNotContain("private", log.DetailsJson);
        Assert.Equal("private", (await read.Users.SingleAsync(u => u.Id == _employee.Id)).CredentialStateReasonNote);
    }

    [Fact]
    public async Task The_owner_requires_a_change_and_a_repeat_is_idempotent()
    {
        var client = Client(Token(_owner, "TenantAdmin"));
        var route = $"/auth/users/{_employee.Id}/credentials/require-change";

        var first = await client.PostAsJsonAsync(route, new { reasonCode = "OfficePolicy" });
        var again = await client.PostAsJsonAsync(route, new { reasonCode = "OfficePolicy" });
        var other = await client.PostAsJsonAsync(route, new { reasonCode = "Other" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(
            "ChangeRequired",
            (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("credentialStatus").GetString()
        );
        Assert.True((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyInState").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ReasonRequired", await CodeAsync(other));
    }

    [Fact]
    public async Task The_internal_portal_endpoints_require_a_change_and_secure()
    {
        var client = PortalClient(ActorToken(_owner));
        var route = $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-credentials";

        var change = await client.PostAsJsonAsync(
            $"{route}/require-change",
            new
            {
                requestedByUserId = _owner.Id,
                reauthenticatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                reasonCode = "SharedPassword",
            }
        );
        var secure = await client.PostAsJsonAsync(
            $"{route}/secure",
            new
            {
                requestedByUserId = _owner.Id,
                reauthenticatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                reasonCode = "SuspectedCompromise",
            }
        );

        Assert.True(change.StatusCode == HttpStatusCode.OK, await change.Content.ReadAsStringAsync());
        Assert.True(secure.StatusCode == HttpStatusCode.OK, await secure.Content.ReadAsStringAsync());
        await using var read = _database.NewContext(_tenant.Id);
        Assert.Equal(
            CredentialStatus.ResetRequired,
            (await read.Users.SingleAsync(u => u.Id == _portal.Id)).CredentialStatus
        );
    }

    // Un motivo desconocido responde InvalidReason (03 §15), no el 400 genérico del binding.
    [Theory]
    [InlineData("credentials/require-change")]
    [InlineData("credentials/secure")]
    [InlineData("mfa/reset")]
    public async Task An_unknown_staff_reason_answers_invalid_reason(string action)
    {
        var response = await Client(Token(_owner, "TenantAdmin"))
            .PostAsJsonAsync($"/auth/users/{_employee.Id}/{action}", new { reasonCode = "NotAReason" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.InvalidReason", await CodeAsync(response));
    }

    [Theory]
    [InlineData("portal-credentials/require-change")]
    [InlineData("portal-credentials/secure")]
    [InlineData("portal-mfa/reset")]
    public async Task An_unknown_portal_reason_answers_invalid_reason(string action)
    {
        var response = await PortalClient(ActorToken(_owner))
            .PostAsJsonAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/{action}",
                new
                {
                    requestedByUserId = _owner.Id,
                    reauthenticatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    reasonCode = "NotAReason",
                }
            );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.InvalidReason", await CodeAsync(response));
    }

    // 03 §9.1: el token de cambio obligatorio solo pasa por su lista blanca; el resto responde 403.
    [Fact]
    public async Task A_password_change_token_is_refused_outside_its_whitelist()
    {
        var client = Client(PasswordChangeToken(_employee));

        var me = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal("Auth.PasswordChangeRequired", await CodeAsync(me));
    }

    private string PasswordChangeToken(User user)
    {
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new("actor_type", "TenantEmployee"),
            new(ClaimTypes.Role, "TenantEmployee"),
            new("sid", Guid.NewGuid().ToString()),
            new("pwd_change", "required"),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_factory.JwtSecret));
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.PasswordChange",
            claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private User NewUser(UserActorType actorType, Guid? onboardingId = null, Guid? customerId = null)
    {
        var user = User.Register(
            _tenant.Id,
            "Sam",
            "Test",
            $"{Guid.NewGuid():N}@example.test",
            "not-a-real-hash",
            actorType,
            customerId,
            onboardingId
        ).Value;
        user.VerifyEmail();
        return user;
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Token de servicio de Customer más el del actor en la cabecera dedicada (SR-03).
    private HttpClient PortalClient(string? actorToken)
    {
        var client = Client(Token(_serviceId, _tenant.Id, "Service", reauth: false));
        if (actorToken is not null)
            client.DefaultRequestHeaders.Add("X-Actor-Authorization", $"Bearer {actorToken}");
        return client;
    }

    private string ActorToken(User user, TimeSpan? reauthAgo = null)
    {
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new("actor_type", user.ActorType.ToString()),
            new("sid", Guid.NewGuid().ToString()),
            new(
                "reauth_at",
                DateTimeOffset.UtcNow.Add(-(reauthAgo ?? TimeSpan.FromMinutes(1))).ToUnixTimeSeconds().ToString()
            ),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_factory.JwtSecret));
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.Services",
            claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private string Token(User user, string actorType, bool reauth = true) =>
        Token(user.Id, user.TenantId, actorType, reauth);

    private string Token(Guid userId, Guid tenantId, string actorType, bool reauth = true)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("actor_type", actorType),
            new(ClaimTypes.Role, actorType),
        };
        if (reauth)
            claims.Add(new Claim("reauth_at", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString()));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_factory.JwtSecret));
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.Services",
            claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    internal sealed class CredentialsApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Jwt:PublicKeyPath"] = string.Empty,
                            ["Jwt:PublicKeyPem"] = string.Empty,
                            ["Jwt:Secret"] = JwtSecret,
                        }
                    )
            );
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    if (
                        hosted.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                    )
                        services.Remove(hosted);
                }
                services.ConfigureDbContext<AuthDbContext>(db => db.UseSqlServer(connectionString));
                services.AddSingleton<IAuthorizationHandler, AllowAllPermissions>();
            });
        }
    }

    private sealed class AllowAllPermissions : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            foreach (var requirement in context.PendingRequirements.ToList())
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
