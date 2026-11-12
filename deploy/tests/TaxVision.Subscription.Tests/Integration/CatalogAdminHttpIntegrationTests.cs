using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// D13 por HTTP, con el host real: solo el admin de plataforma entra al catálogo, y una operación peligrosa exige el
/// token de su vista previa y un motivo. Solo se usan pedidos que NO cambian la base local: vista previa, falta de
/// motivo y token viejo.
/// </summary>
public sealed class CatalogAdminHttpIntegrationTests(SubscriptionApiFactory factory)
    : IClassFixture<SubscriptionApiFactory>
{
    private static readonly string OffSale = $"admin/subscription/plans/{PlanCatalog.EnterpriseId}/off-sale";

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("TenantEmployee")]
    [InlineData("CustomerPortal")]
    public async Task Only_the_platform_admin_reaches_the_catalog(string actorType)
    {
        var client = Client(actorType);

        var preview = await client.PostAsync($"{OffSale}/preview", content: null);
        var audit = await client.GetAsync("admin/subscription/catalog/audit");

        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, audit.StatusCode);
    }

    [Fact]
    public async Task The_platform_admin_gets_a_preview_with_a_token()
    {
        var response = await Client("PlatformAdmin").PostAsync($"{OffSale}/preview", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<PreviewBody>();
        Assert.False(string.IsNullOrWhiteSpace(preview!.ConfirmationToken));
        Assert.Equal("enterprise", preview.PlanCode);
    }

    [Fact]
    public async Task Confirming_without_a_reason_is_a_bad_request()
    {
        var client = Client("PlatformAdmin");
        var preview = await (
            await client.PostAsync($"{OffSale}/preview", content: null)
        ).Content.ReadFromJsonAsync<PreviewBody>();

        var response = await client.PostAsJsonAsync(
            OffSale,
            new { confirmationToken = preview!.ConfirmationToken, reason = "" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Catalog.ReasonRequired", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Confirming_with_a_stale_token_is_a_conflict()
    {
        var response = await Client("PlatformAdmin")
            .PostAsJsonAsync(OffSale, new { confirmationToken = "stale-token", reason = "Integration test" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Catalog.ConfirmationStale", await response.Content.ReadAsStringAsync());
    }

    private HttpClient Client(string actorType)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokenFactory.Mint(factory, Guid.NewGuid(), Guid.NewGuid(), actorType)
        );
        return client;
    }

    private sealed record PreviewBody(string PlanCode, int OfficesOnPlan, string ConfirmationToken);
}
