using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// Fase 10 (03_Plan_De_Fases.md §Fase 10, guardrail RateLimit #10) — prueba end-to-end real contra
/// Notes.Api: SQL Server/Redis/RabbitMQ locales reales, sin mocks de infraestructura. Un test por
/// categoría real representativa del inventario de este servicio (F, G) — mismo criterio de
/// cobertura que <c>TaxVision.Correspondence.Tests.Integration.RateLimitIntegrationTests</c>
/// (Fase 4.9). <c>Pin</c> (G) se prueba sobre un noteId sintético que no existe — el filtro de
/// rate limit corre ANTES del handler, así que el 404/error de negocio no bloquea el conteo, mismo
/// criterio ya usado en Correspondence. Cada test method usa un tenantId/userId nuevo
/// (<see cref="Guid.NewGuid"/>) para no compartir contador de <c>IRateCounter</c> con otra
/// corrida — el contador vive en Redis y sobrevive al proceso de test.
/// </summary>
public sealed class RateLimitIntegrationTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public RateLimitIntegrationTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Mine_trips_with_user_layer_and_limit_300()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(
            () => client.GetAsync("/notes/mine?page=1&size=20"),
            maxAttempts: 600
        );

        await AssertTripped(tripped, expectedPolicy: "notes.f.list", expectedLayer: "user", expectedLimit: 300);
    }

    [Fact]
    public async Task Pin_trips_with_user_layer_and_limit_60()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(
            () => client.PostAsync($"/notes/{noteId}/pin", content: null),
            maxAttempts: 120
        );

        await AssertTripped(tripped, expectedPolicy: "notes.g.write", expectedLayer: "user", expectedLimit: 60);
    }

    /// <summary>
    /// Dispara requests hasta encontrar el primer 429 — no asume un índice fijo de disparo. Mismo
    /// criterio que Correspondence: las categorías Token bucket (F/G) toleran ráfaga y refillan de
    /// forma continua mientras corre el loop, así que el margen (maxAttempts &gt; limit) cubre ese
    /// refill esperado sin dejar de verificar que el límite se aplica de verdad.
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
