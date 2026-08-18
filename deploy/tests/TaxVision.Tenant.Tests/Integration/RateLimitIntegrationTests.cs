using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Fase 4.2 del plan de rate limiting (Plan_Implementacion_Fases.md §4) — prueba end-to-end real
/// contra Tenant.Api: SQL Server/Redis/RabbitMQ locales reales, sin mocks de infraestructura.
/// Un test por categoría real del inventario de este servicio (F, G, I) — mismo criterio de
/// cobertura que <c>TaxVision.Customer.Tests.Integration.RateLimitIntegrationTests</c> (Fase 3/4.1).
/// Cada test method usa un tenantId/userId nuevo (<see cref="Guid.NewGuid"/>) para no compartir
/// contador de <c>IRateCounter</c> con otra corrida — el contador vive en Redis y sobrevive al
/// proceso de test.
/// </summary>
public sealed class RateLimitIntegrationTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory factory;

    public RateLimitIntegrationTests(TenantApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task TenantList_trips_with_user_layer_and_limit_300()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(() => client.GetAsync("/tenants?page=1&size=20"), maxAttempts: 600);

        await AssertTripped(tripped, expectedPolicy: "tenant.f.list", expectedLayer: "user", expectedLimit: 300);
    }

    [Fact]
    public async Task TenantStatusChange_trips_with_user_layer_and_limit_60()
    {
        // Id inexistente igual cuenta — el filtro de [RateLimit] corre antes que el handler
        // (mismo criterio que CustomerUpdate_trips_with_user_layer_and_limit_60 en Customer.Tests).
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);
        var randomTenantId = Guid.NewGuid();

        var tripped = await FireUntilTrippedAsync(
            () => client.PatchAsJsonAsync($"/tenants/{randomTenantId}/status", new { Status = "Suspended" }),
            maxAttempts: 120
        );

        await AssertTripped(
            tripped,
            expectedPolicy: "tenant.g.status_change",
            expectedLayer: "user",
            expectedLimit: 60
        );
    }

    [Fact]
    public async Task TenantLogoUpload_trips_with_user_layer_and_limit_10()
    {
        // tenant.i.logo_upload particiona solo por tenant (claim tenant_id del JWT, no por el
        // {tenantId} de la ruta) — ver comentario de TenantLogoUpload en RateLimitPolicyCatalog.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);
        var randomTenantId = Guid.NewGuid();

        var tripped = await FireUntilTrippedAsync(
            async () =>
            {
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent([1, 2, 3, 4]);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(fileContent, "file", "logo.png");

                return await client.PutAsync($"/tenants/{randomTenantId}/logo", content);
            },
            maxAttempts: 20
        );

        // "user" es el nombre de capa que devuelve la capa primaria del evaluador (§0 del plan) —
        // acá la partición primaria es por tenant, pero el layer sigue reportándose como "user"
        // porque es la capa primaria (no overlay), mismo criterio que el resto del catálogo.
        await AssertTripped(tripped, expectedPolicy: "tenant.i.logo_upload", expectedLayer: "user", expectedLimit: 10);
    }

    /// <summary>
    /// Dispara requests hasta encontrar el primer 429 — no asume un índice fijo de disparo.
    /// Necesario desde que <see cref="TieredRateLimitEvaluator"/> honra el algoritmo declarado por
    /// la política (auditoría post-Fase-9, hallazgo #8): categorías Token bucket (F/G) toleran
    /// ráfaga y refillan de forma continua mientras corre el loop — un test secuencial de N
    /// requests reales (con I/O real a SQL/Redis) puede tardar más de 1 segundo en completar N
    /// vueltas, y ese tiempo real transcurrido regala tokens nuevos, corriendo el punto de disparo
    /// más allá de N. El margen (maxAttempts &gt; limit) cubre ese refill esperado sin dejar de
    /// verificar que el límite se aplica de verdad.
    /// </summary>
    private static async Task<HttpResponseMessage> FireUntilTrippedAsync(
        Func<Task<HttpResponseMessage>> send,
        int maxAttempts
    )
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            var response = await send().ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return response;
        }

        throw new InvalidOperationException($"Rate limit did not trip within {maxAttempts} attempts.");
    }

    private HttpClient CreateAuthenticatedClient(Guid tenantId, Guid userId)
    {
        var client = factory.CreateClient();
        var token = JwtTestTokenFactory.Mint(factory, tenantId, userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertTripped(
        HttpResponseMessage response,
        string expectedPolicy,
        string expectedLayer,
        int expectedLimit
    )
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        Assert.True(response.Headers.TryGetValues("Retry-After", out _), "Retry-After header missing.");
        Assert.Equal(expectedPolicy, response.Headers.GetValues("X-RateLimit-Policy").Single());
        Assert.Equal(expectedLayer, response.Headers.GetValues("X-RateLimit-Layer").Single());
        Assert.Equal(expectedLimit.ToString(), response.Headers.GetValues("X-RateLimit-Limit").Single());
        Assert.Equal("0", response.Headers.GetValues("X-RateLimit-Remaining").Single());
        Assert.True(response.Headers.TryGetValues("X-RateLimit-Reset", out _), "X-RateLimit-Reset header missing.");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RateLimit.Exceeded", body.GetProperty("code").GetString());
        Assert.Equal(expectedPolicy, body.GetProperty("policy").GetString());
        Assert.Equal(expectedLayer, body.GetProperty("layer").GetString());
    }
}
