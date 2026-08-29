using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Fase 3 del plan de rate limiting (Plan_Implementacion_Fases.md §6.3) — prueba end-to-end real
/// contra Customer.Api: SQL Server/Redis/RabbitMQ locales reales, sin mocks de infraestructura.
/// Cada test method usa un tenantId/userId nuevo (<see cref="Guid.NewGuid"/>) para no compartir
/// contador de <c>IRateCounter</c> con otra corrida — el contador vive en Redis y sobrevive al
/// proceso de test.
/// </summary>
public sealed class RateLimitIntegrationTests : IClassFixture<CustomerApiFactory>
{
    private readonly CustomerApiFactory factory;

    public RateLimitIntegrationTests(CustomerApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task CustomerCreate_trips_with_user_layer_and_limit_60()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(
            () =>
                client.PostAsJsonAsync(
                    "/customers",
                    new
                    {
                        Kind = "Individual",
                        FirstName = "RateLimit",
                        LastName = "Tester",
                        PrimaryEmail = "ratelimit.create.test@ratelimit-test.local",
                        Language = "En",
                        PreferredChannel = "Email",
                    }
                ),
            maxAttempts: 120
        );

        await AssertTripped(tripped, expectedPolicy: "customer.g.create", expectedLayer: "user", expectedLimit: 60);
    }

    [Fact]
    public async Task CustomerGetById_trips_with_user_layer_and_limit_300()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);
        var randomCustomerId = Guid.NewGuid();

        var tripped = await FireUntilTrippedAsync(
            () => client.GetAsync($"/customers/{randomCustomerId}"),
            maxAttempts: 600
        );

        await AssertTripped(tripped, expectedPolicy: "customer.f.get", expectedLayer: "user", expectedLimit: 300);
    }

    [Fact]
    public async Task FiscalReveal_trips_with_user_layer_and_limit_5()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);
        var randomCustomerId = Guid.NewGuid();

        var tripped = await FireUntilTrippedAsync(
            () => client.GetAsync($"/customers/{randomCustomerId}/fiscal-profile/tax-identifier"),
            maxAttempts: 20
        );

        await AssertTripped(
            tripped,
            expectedPolicy: "customer.n.fiscal_reveal",
            expectedLayer: "user",
            expectedLimit: 5
        );
    }

    [Fact]
    public async Task CustomerUpdate_trips_with_user_layer_and_limit_60()
    {
        // Fase 4.1: customer.g.write es compartida por los ~17 endpoints de escritura simple
        // sobre un customer existente — este test prueba el wiring vía PATCH /customers/{id}
        // (un id inexistente igual cuenta, el filtro corre antes que el handler).
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);
        var randomCustomerId = Guid.NewGuid();

        var tripped = await FireUntilTrippedAsync(
            () =>
                client.PatchAsJsonAsync(
                    $"/customers/{randomCustomerId}",
                    new
                    {
                        Language = "En",
                        PreferredChannel = "Email",
                        PrimaryEmail = "ratelimit.update.test@ratelimit-test.local",
                    }
                ),
            maxAttempts: 120
        );

        await AssertTripped(tripped, expectedPolicy: "customer.g.write", expectedLayer: "user", expectedLimit: 60);
    }

    [Fact]
    public async Task BulkStatusChange_trips_with_user_layer_and_limit_12()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(
            () =>
                client.PostAsJsonAsync(
                    "/customers/bulk/activate",
                    new { CustomerIds = new[] { Guid.NewGuid() }, Reason = (string?)null }
                ),
            maxAttempts: 40
        );

        await AssertTripped(
            tripped,
            expectedPolicy: "customer.i.bulk_status_change",
            expectedLayer: "user",
            expectedLimit: 12
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
