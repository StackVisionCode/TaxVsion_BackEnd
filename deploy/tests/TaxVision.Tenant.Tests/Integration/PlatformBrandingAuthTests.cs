using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Fase 4 (marca del sistema) — la propiedad de seguridad central: SOLO el PlatformAdmin toca la
/// marca del sistema. Prueba end-to-end contra Tenant.Api real. El TenantAdmin recibe 403 por el
/// <c>[AllowActorTypes(PlatformAdmin)]</c> a nivel de clase, antes incluso del chequeo de permiso.
/// </summary>
public sealed class PlatformBrandingAuthTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory factory;

    public PlatformBrandingAuthTests(TenantApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Platform_admin_can_read_the_system_brand()
    {
        var client = ClientFor("PlatformAdmin");

        var response = await client.GetAsync("/platform/branding/Crm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // La marca del sistema sembrada (Fase 1): primary #1E466B.
        var primary = body.GetProperty("colors")
            .EnumerateArray()
            .Single(c => c.GetProperty("token").GetString() == "Primary");
        Assert.Equal("#1E466B", primary.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Tenant_admin_is_forbidden_from_the_system_brand()
    {
        var client = ClientFor("TenantAdmin");

        var response = await client.GetAsync("/platform/branding/Crm");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_is_forbidden_from_writing_the_system_brand()
    {
        var client = ClientFor("TenantAdmin");

        var response = await client.PutAsJsonAsync("/platform/branding/Crm/colors", new { Primary = "#123456" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_surface_is_a_400_for_platform_admin()
    {
        var client = ClientFor("PlatformAdmin");

        var response = await client.GetAsync("/platform/branding/Mobile");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient ClientFor(string actorType)
    {
        var client = factory.CreateClient();
        var token = JwtTestTokenFactory.MintActor(factory, Guid.NewGuid(), Guid.NewGuid(), actorType);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
