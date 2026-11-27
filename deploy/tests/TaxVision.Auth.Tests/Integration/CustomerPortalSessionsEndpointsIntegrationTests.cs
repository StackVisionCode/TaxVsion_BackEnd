using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// SR-11: los M2M de sesiones e invitación del portal exigen el token del actor reenviado por Customer. Actor,
/// tipo de actor, oficina y permiso (con Deny) salen del token y de Auth, no de <c>requestedByUserId</c>.
/// </summary>
public sealed class CustomerPortalSessionsEndpointsIntegrationTests : IAsyncLifetime
{
    private AuthTestDatabase _database = null!;
    private UserCredentialsEndpointsIntegrationTests.CredentialsApiFactory _factory = null!;
    private Tenant _tenant = null!;
    private User _owner = null!;
    private User _coAdmin = null!;
    private User _viewer = null!;
    private User _manager = null!;
    private User _portal = null!;
    private User _platformAdmin = null!;
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _platformCustomerId = Guid.NewGuid();
    private readonly Guid _serviceId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        _tenant = Tenant
            .Register(Guid.NewGuid(), $"t{Guid.NewGuid():N}"[..20], "Office", TenantKind.Customer, "America/New_York")
            .Value;
        _owner = NewUser(_tenant.Id, UserActorType.TenantAdmin, onboardingId: Guid.NewGuid());
        _coAdmin = NewUser(_tenant.Id, UserActorType.TenantAdmin);
        _viewer = NewUser(_tenant.Id, UserActorType.TenantEmployee);
        _manager = NewUser(_tenant.Id, UserActorType.TenantEmployee);
        _portal = NewUser(_tenant.Id, UserActorType.CustomerPortal, customerId: _customerId);

        var adminRole = Role.Create(_tenant.Id, Role.SystemTenantAdmin, null, isSystem: true).Value;
        adminRole.SetPermissions(
            [
                PermissionCatalog.IdOf(PermissionCatalog.CustomersView),
                PermissionCatalog.IdOf(PermissionCatalog.CustomersManage),
                PermissionCatalog.IdOf(PermissionCatalog.CustomersPortalSessionsLocationView),
            ],
            seeding: true
        );
        var viewerRole = Role.Create(_tenant.Id, "Viewer", null, isSystem: false).Value;
        viewerRole.SetPermissions([PermissionCatalog.IdOf(PermissionCatalog.CustomersView)], seeding: true);
        var managerRole = Role.Create(_tenant.Id, "Manager", null, isSystem: false).Value;
        managerRole.SetPermissions(
            [
                PermissionCatalog.IdOf(PermissionCatalog.CustomersView),
                PermissionCatalog.IdOf(PermissionCatalog.CustomersManage),
            ],
            seeding: true
        );

        await using (var db = _database.NewContext(_tenant.Id))
        {
            db.Add(_tenant);
            db.Users.AddRange(_owner, _coAdmin, _viewer, _manager, _portal);
            db.Roles.AddRange(adminRole, viewerRole, managerRole);
            db.UserRoles.AddRange(
                UserRole.Create(_owner.Id, adminRole.Id),
                UserRole.Create(_coAdmin.Id, adminRole.Id),
                UserRole.Create(_viewer.Id, viewerRole.Id),
                UserRole.Create(_manager.Id, managerRole.Id)
            );
            db.Add(UserSession.Start(_tenant.Id, _portal.Id, "Phone", "UA", "203.0.113.9"));
            await db.SaveChangesAsync();
        }

        // El PlatformAdmin opera con su propio tenant de plataforma (sin roles: bypass de permisos).
        _platformAdmin = NewUser(PlatformTenant.Id, UserActorType.PlatformAdmin);
        await using (var db = _database.NewContext(PlatformTenant.Id))
        {
            db.Users.Add(_platformAdmin);
            await db.SaveChangesAsync();
        }

        _factory = new UserCredentialsEndpointsIntegrationTests.CredentialsApiFactory(_database.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Theory]
    [InlineData("GET", "portal-sessions")]
    [InlineData("DELETE", "portal-sessions/{session}?requestedByUserId={owner}")]
    [InlineData("DELETE", "portal-sessions?requestedByUserId={owner}&forgetTrustedDevices=false")]
    [InlineData("POST", "portal-invitation")]
    public async Task A_call_without_the_actor_token_is_rejected_and_audited(string method, string resource)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/"
                + resource.Replace("{session}", Guid.NewGuid().ToString()).Replace("{owner}", _owner.Id.ToString())
        );
        if (method == "POST")
            request.Content = JsonContent.Create(new { email = "client@example.test", requestedByUserId = _owner.Id });

        var response = await PortalClient(_tenant.Id, actorToken: null).SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorTokenInvalid", await CodeAsync(response));
        await using var read = _database.NewContext(_tenant.Id);
        var denied = await read.AuthAuditLogs.SingleAsync(l =>
            l.Action == "auth.credential_admin.denied" && l.TargetId == _portal.Id && !l.Success
        );
        // OBS-04: el actor es el servicio que llamó; el solicitante declarado solo va en los detalles.
        Assert.Equal(_serviceId, denied.UserId);
        Assert.Equal("Service", denied.ActorType);
        var claimed = JsonDocument.Parse(denied.DetailsJson!).RootElement.GetProperty("claimedRequestedByUserId");
        if (method == "GET")
            Assert.Equal(JsonValueKind.Null, claimed.ValueKind);
        else
            Assert.Equal(_owner.Id, claimed.GetGuid());
    }

    // SR-05: la ubicación también la decide Auth con el permiso del actor (Customer la vuelve a ocultar).
    [Fact]
    public async Task An_employee_lists_the_sessions_without_location_unless_granted()
    {
        var route = $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-sessions";

        var viewer = await PortalClient(_tenant.Id, ActorToken(_viewer)).GetAsync(route);
        var owner = await PortalClient(_tenant.Id, ActorToken(_owner)).GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.True(await FirstLocationHiddenAsync(viewer));
        Assert.False(await FirstLocationHiddenAsync(owner));
    }

    [Fact]
    public async Task An_employee_without_manage_cannot_close_sessions()
    {
        var response = await PortalClient(_tenant.Id, ActorToken(_viewer))
            .DeleteAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-sessions?requestedByUserId={_viewer.Id}"
            );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorNotAllowed", await CodeAsync(response));
    }

    [Fact]
    public async Task A_deny_on_customers_manage_is_enforced_by_auth()
    {
        await using (var db = _database.NewContext(_tenant.Id))
        {
            db.UserPermissionDenies.Add(
                UserPermissionDeny.Create(_manager.Id, PermissionCatalog.IdOf(PermissionCatalog.CustomersManage))
            );
            await db.SaveChangesAsync();
        }

        var response = await PortalClient(_tenant.Id, ActorToken(_manager))
            .DeleteAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-sessions?requestedByUserId={_manager.Id}"
            );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorNotAllowed", await CodeAsync(response));
        await using var read = _database.NewContext(_tenant.Id);
        var denied = await read.AuthAuditLogs.SingleAsync(l =>
            l.Action == "auth.credential_admin.denied" && l.UserId == _manager.Id
        );
        Assert.Equal("TenantEmployee", denied.ActorType);
    }

    [Fact]
    public async Task The_actor_token_must_belong_to_the_requester()
    {
        var response = await PortalClient(_tenant.Id, ActorToken(_coAdmin))
            .DeleteAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-sessions?requestedByUserId={_owner.Id}"
            );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorTokenInvalid", await CodeAsync(response));
        await using var read = _database.NewContext(_tenant.Id);
        var denied = await read.AuthAuditLogs.SingleAsync(l => l.Action == "auth.credential_admin.denied");
        Assert.Equal(_coAdmin.Id, denied.UserId);
        Assert.Equal("TenantAdmin", denied.ActorType);
        Assert.Equal(
            _owner.Id,
            JsonDocument.Parse(denied.DetailsJson!).RootElement.GetProperty("claimedRequestedByUserId").GetGuid()
        );
    }

    [Fact]
    public async Task An_employee_cannot_issue_the_portal_invitation()
    {
        var response = await PortalClient(_tenant.Id, ActorToken(_manager))
            .PostAsJsonAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{Guid.NewGuid()}/portal-invitation",
                new { email = "new.client@example.test", requestedByUserId = _manager.Id }
            );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Auth.CredentialAdmin.ActorTokenInvalid", await CodeAsync(response));
    }

    [Fact]
    public async Task The_owner_issues_the_invitation_as_the_token_actor()
    {
        var response = await PortalClient(_tenant.Id, ActorToken(_owner))
            .PostAsJsonAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{Guid.NewGuid()}/portal-invitation",
                new { email = "new.client@example.test", requestedByUserId = _owner.Id }
            );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var read = _database.NewContext(_tenant.Id);
        var log = await read.AuthAuditLogs.SingleAsync(l => l.Action == "auth.invitation.created");
        Assert.Equal(_owner.Id, log.UserId);
        Assert.Equal("TenantAdmin", log.ActorType);
    }

    [Fact]
    public async Task The_owner_closes_the_sessions_and_the_audit_keeps_the_actor_type()
    {
        var response = await PortalClient(_tenant.Id, ActorToken(_owner))
            .DeleteAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-sessions?requestedByUserId={_owner.Id}"
            );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var read = _database.NewContext(_tenant.Id);
        var log = await read.AuthAuditLogs.SingleAsync(l => l.Action == "auth.session.all_revoked");
        Assert.Equal(_owner.Id, log.UserId);
        Assert.Equal("TenantAdmin", log.ActorType);
    }

    // El tenant de plataforma no tiene cuentas de portal: pasar el gate se ve como el 409 del dominio.
    [Fact]
    public async Task A_platform_admin_passes_with_its_platform_tenant_and_not_with_an_office()
    {
        var route = $"/internal/tenants/{PlatformTenant.Id}/customers/{_platformCustomerId}/portal-sessions";

        var list = await PortalClient(PlatformTenant.Id, ActorToken(_platformAdmin)).GetAsync(route);
        var revoke = await PortalClient(PlatformTenant.Id, ActorToken(_platformAdmin))
            .DeleteAsync($"{route}?requestedByUserId={_platformAdmin.Id}");
        var office = await PortalClient(_tenant.Id, ActorToken(_platformAdmin))
            .DeleteAsync(
                $"/internal/tenants/{_tenant.Id}/customers/{_customerId}/portal-sessions?requestedByUserId={_platformAdmin.Id}"
            );

        Assert.Equal("Auth.NoPortalAccount", await CodeAsync(list));
        Assert.Equal("Auth.NoPortalAccount", await CodeAsync(revoke));
        Assert.Equal(HttpStatusCode.Unauthorized, office.StatusCode);
        await using var read = _database.NewContext(_tenant.Id);
        var denied = await read.AuthAuditLogs.SingleAsync(l =>
            l.Action == "auth.credential_admin.denied" && l.UserId == _platformAdmin.Id
        );
        Assert.Equal("PlatformAdmin", denied.ActorType);
        Assert.Equal(0, await read.AuthAuditLogs.CountAsync(l => l.Action == "auth.session.all_revoked"));
    }

    private User NewUser(Guid tenantId, UserActorType actorType, Guid? onboardingId = null, Guid? customerId = null)
    {
        var user = User.Register(
            tenantId,
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

    // Token de servicio de Customer más el del actor en la cabecera dedicada.
    private HttpClient PortalClient(Guid tenantId, string? actorToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Mint(_serviceId, tenantId, "Service", withSession: false)
        );
        if (actorToken is not null)
            client.DefaultRequestHeaders.Add("X-Actor-Authorization", $"Bearer {actorToken}");
        return client;
    }

    private string ActorToken(User user) => Mint(user.Id, user.TenantId, user.ActorType.ToString(), withSession: true);

    private string Mint(Guid userId, Guid tenantId, string actorType, bool withSession)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("actor_type", actorType),
            new(ClaimTypes.Role, actorType),
        };
        if (withSession)
            claims.Add(new Claim("sid", Guid.NewGuid().ToString()));
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.Services",
            claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_factory.JwtSecret)),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static async Task<bool> FirstLocationHiddenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items")[0].GetProperty("locationHidden").GetBoolean();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
