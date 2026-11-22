using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Domain.Permissions;
using TaxVision.Tenant.Infrastructure.Persistence;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Preferencias contra Tenant.Api real con actores reales: la plantilla de la oficina exige <c>settings.manage</c>
/// (proyección local de permisos), el portal no entra y el conflicto de versión es 409.
/// </summary>
public sealed class WorkspacePreferencesAuthTests(TenantApiFactory factory) : IClassFixture<TenantApiFactory>
{
    private const string MeRoute = "/team/me/workspace-preferences/crm-dashboard-layout";
    private const string OfficeRoute = "/team/workspace-preferences/crm-dashboard-layout/office-default";

    private static readonly object Body = new
    {
        schemaVersion = 1,
        expectedVersion = 0,
        payload = new[]
        {
            new
            {
                id = "tasks",
                size = "m",
                hidden = false,
            },
        },
    };

    [Fact]
    public async Task An_employee_without_settings_manage_cannot_touch_the_office_default()
    {
        var client = await EmployeeAsync([]);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(OfficeRoute, Body)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"{OfficeRoute}?expectedVersion=0")).StatusCode
        );
    }

    [Fact]
    public async Task An_employee_with_settings_manage_sets_the_office_default()
    {
        var client = await EmployeeAsync(["settings.manage"]);

        var response = await client.PutAsJsonAsync(OfficeRoute, Body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, view.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task An_employee_saves_and_reads_their_own_layout_and_a_stale_version_is_409()
    {
        var client = await EmployeeAsync([]);

        var first = await client.GetFromJsonAsync<JsonElement>(MeRoute);
        Assert.Equal("default", first.GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(MeRoute, Body)).StatusCode);

        var stale = await client.PutAsJsonAsync(MeRoute, Body);
        var saved = await client.GetFromJsonAsync<JsonElement>(MeRoute);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("user", saved.GetProperty("source").GetString());
        Assert.Equal(1, saved.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task An_unknown_key_is_400()
    {
        var client = await EmployeeAsync([]);

        var response = await client.GetAsync("/team/me/workspace-preferences/crm-other");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // El PlatformAdmin pasa [AllowActorTypes] en toda la flota (ActorTypeAuthorizationFilter): no se prueba aquí.
    [Fact]
    public async Task Portal_clients_are_forbidden()
    {
        var client = ClientFor(Guid.NewGuid(), Guid.NewGuid(), "CustomerPortal");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(MeRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(OfficeRoute, Body)).StatusCode);
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var response = await factory.CreateClient().GetAsync(MeRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Empleado con su fila de permisos en la proyección local (versión 0 = la del token sin perm_v).
    private async Task<HttpClient> EmployeeAsync(IReadOnlyCollection<string> permissions)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = new TenantDbContext(
            new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(TenantIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );
        db.UserPermissionsProjections.Add(UserPermissionsProjection.Create(tenantId, userId, 0, permissions, []));
        await db.SaveChangesAsync();
        return ClientFor(tenantId, userId, "TenantEmployee");
    }

    private HttpClient ClientFor(Guid tenantId, Guid userId, string actorType)
    {
        var client = factory.CreateClient();
        var token = JwtTestTokenFactory.MintActor(factory, tenantId, userId, actorType);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
