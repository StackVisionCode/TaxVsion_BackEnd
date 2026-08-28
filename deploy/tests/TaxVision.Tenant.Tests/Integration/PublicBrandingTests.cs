using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Fase 5 — branding ANÓNIMO pre-login contra Tenant.Api real. Sin token (login page). Verifica el
/// criterio de cierre (curl anónimo devuelve branding) y las propiedades de seguridad: anti-enumeración
/// (slug desconocido → marca del sistema con 200) y que un fileId inválido/inexistente da 404.
/// </summary>
public sealed class PublicBrandingTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory factory;

    public PublicBrandingTests(TenantApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Unknown_slug_returns_the_system_brand_with_200_not_404()
    {
        var client = factory.CreateClient(); // anónimo, sin Authorization

        var response = await client.GetAsync("/tenants/branding/public/definitely-not-an-office?surface=Crm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("#1E466B", body.GetProperty("primary").GetString());
        Assert.Equal("#67BAF4", body.GetProperty("accent").GetString());
    }

    [Fact]
    public async Task Missing_surface_is_a_400()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/tenants/branding/public/whatever");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_non_configurable_surface_is_a_400()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/tenants/branding/public/whatever?surface=Mobile");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Asset_with_a_non_guid_token_is_a_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/tenants/branding/assets/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Asset_for_an_unknown_file_id_is_a_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/tenants/branding/assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
