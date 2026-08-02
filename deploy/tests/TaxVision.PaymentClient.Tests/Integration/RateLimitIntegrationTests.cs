using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// Fase 4.14 del plan de rate limiting (Plan_Implementacion_Fases.md §4) — prueba end-to-end real
/// contra PaymentClient.Api: SQL Server/Redis/RabbitMQ locales reales, sin mocks de
/// infraestructura. Un test por categoría real de este servicio expuesta bajo un controller
/// exento de <c>TenantStatusGateMiddleware</c> (F, H) — mismo criterio de cobertura que
/// <c>TaxVision.PaymentApp.Tests.Integration.RateLimitIntegrationTests</c> (Fase 4.13). Las
/// categorías G (Payouts/PaymentLinks/Recurring/Config) y L (ConnectAccount.Onboard/
/// TenantPayments.Charge) requieren un tenant activo real en la BD local (no exentas del gate) y
/// quedan cubiertas por inspección de código + el mismo evaluador ya verificado en Fase 4.1-4.13,
/// consistente con el criterio de Fase 4.12/4.13. <c>PaymentClientAdminController</c> se eligió
/// porque PlatformAdmin bypassea [HasPermission] Y <c>/payments-client/admin</c> está exento de
/// <c>TenantStatusGateMiddleware</c>, así que un tenant/user sintético nuevo no choca contra
/// ninguno de los dos gates. Cada test method usa un tenantId/userId nuevo (<see cref="Guid.NewGuid"/>)
/// para no compartir contador de <c>IRateCounter</c> con otra corrida — el contador vive en Redis
/// y sobrevive al proceso de test. <c>ExportCsv_trips_at_21st_request</c> encontró un bug real en
/// <c>RateLimitAttribute.WriteRateLimitResponse</c> (BuildingBlocks.Web, compartido por todos los
/// servicios): un <c>ObjectResult</c> JSON sin <c>ContentTypes</c> explícito hereda la restricción
/// de <c>[Produces("text/csv")]</c> de la acción y devuelve 406 en vez de 429 — corregido fijando
/// <c>ContentTypes = { "application/json" }</c> en el body de error.
/// </summary>
public sealed class RateLimitIntegrationTests : IClassFixture<PaymentClientApiFactory>
{
    private readonly PaymentClientApiFactory factory;

    public RateLimitIntegrationTests(PaymentClientApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task SearchAllTenants_trips_with_user_layer_and_limit_300()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(
            () => client.GetAsync("/payments-client/admin/payments?page=1&pageSize=10"),
            maxAttempts: 600
        );

        await AssertTripped(
            tripped,
            expectedPolicy: "payment_client.f.admin_read",
            expectedLayer: "user",
            expectedLimit: 300
        );
    }

    [Fact]
    public async Task ExportCsv_trips_with_user_layer_and_limit_20()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var tripped = await FireUntilTrippedAsync(
            () => client.GetAsync("/payments-client/admin/payments/export"),
            maxAttempts: 40
        );

        await AssertTripped(
            tripped,
            expectedPolicy: "payment_client.h.admin_export",
            expectedLayer: "user",
            expectedLimit: 20
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
