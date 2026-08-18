using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Fase 4.6 del plan de rate limiting (Plan_Implementacion_Fases.md §4) — prueba end-to-end real
/// contra CloudStorage.Api: SQL Server/Redis/RabbitMQ locales reales, sin mocks de
/// infraestructura. Un test por categoría real del inventario HTTP de este servicio (F, G) —
/// mismo criterio de cobertura que <c>TaxVision.Scribe.Tests.Integration.RateLimitIntegrationTests</c>
/// (Fase 4.5). El resto de las 37 acciones anotadas quedan fuera de este archivo por necesitar
/// fixtures más pesados (archivos/carpetas/shares reales ya creados) — cubiertas por inspección
/// de código + el mismo evaluador ya verificado en Fase 4.1-4.5. Cada test method usa un
/// tenantId/userId nuevo (<see cref="Guid.NewGuid"/>) para no compartir contador de
/// <c>IRateCounter</c> con otra corrida — el contador vive en Redis y sobrevive al proceso de
/// test.
/// </summary>
public sealed class RateLimitIntegrationTests : IClassFixture<CloudStorageApiFactory>
{
    private readonly CloudStorageApiFactory factory;

    public RateLimitIntegrationTests(CloudStorageApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task FoldersContents_trips_with_user_layer_and_limit_300()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(() => client.GetAsync("/storage/folders"), maxAttempts: 600);

        await AssertTripped(
            tripped,
            expectedPolicy: "cloudstorage.f.folder_browse",
            expectedLayer: "user",
            expectedLimit: 300
        );
    }

    [Fact]
    public async Task SetPublicSharingPolicy_trips_with_user_layer_and_limit_60()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(
            () => client.PutAsJsonAsync("/storage/settings/public-sharing", new { Allow = true }),
            maxAttempts: 120
        );

        await AssertTripped(
            tripped,
            expectedPolicy: "cloudstorage.g.settings_manage",
            expectedLayer: "user",
            expectedLimit: 60
        );
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
