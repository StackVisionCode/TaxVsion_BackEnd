using System.Net;
using System.Text;
using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// Antes un fallo al traer el catálogo de Subscription devolvía un catálogo VACÍO que quedaba cacheado
/// 5 minutos: todos los tenants caían a la cuota base (la mitad de starter) aunque Subscription hubiera
/// vuelto enseguida. Ahora se sirve el último catálogo bueno y, sin uno, se reintenta a los 30 s.
/// </summary>
public sealed class HttpPlanRateLimitReaderTests
{
    private const string CatalogKey = "ratelimit:subscription-plan-rate-limits-catalog";
    private const string UnavailableKey = CatalogKey + ":unavailable";

    private sealed class FakeCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> store = [];

        public void Expire(string key) => store.Remove(key);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(store.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            store.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        )
        {
            var cached = await GetAsync<T>(key, ct);
            if (cached is not null)
                return cached;
            var value = await factory(ct); // si lanza, no se cachea — igual que RedisCacheService
            await SetAsync(key, value, ttl, ct);
            return value;
        }
    }

    private sealed class FixedTokenAcquirer : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("service-token");
    }

    private sealed class QueuedHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> queue = new(statuses);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var status = queue.Dequeue();
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.OK)
                response.Content = new StringContent(
                    """[{"planCode":"pro","category":"H","multiplierOverride":5,"hardOverridePerMinute":null}]""",
                    Encoding.UTF8,
                    "application/json"
                );
            return Task.FromResult(response);
        }
    }

    private static HttpPlanRateLimitReader Reader(QueuedHandler handler, FakeCacheService cache) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://subscription/") },
            new FixedTokenAcquirer(),
            cache,
            NullLogger<HttpPlanRateLimitReader>.Instance
        );

    [Fact]
    public async Task Un_refresco_fallido_sirve_el_ultimo_catalogo_bueno()
    {
        var cache = new FakeCacheService();
        var handler = new QueuedHandler(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        var reader = Reader(handler, cache);

        Assert.Equal(5m, (await reader.GetAsync("pro", RateLimitCategory.H))!.MultiplierOverride);

        cache.Expire(CatalogKey); // vencen los 5 min → el refresco falla
        var snapshot = await reader.GetAsync("pro", RateLimitCategory.H);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(5m, snapshot!.MultiplierOverride);
    }

    [Fact]
    public async Task Sin_catalogo_bueno_un_fallo_no_se_cachea_como_vacio_y_se_reintenta_tras_el_backoff()
    {
        var cache = new FakeCacheService();
        var handler = new QueuedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var reader = Reader(handler, cache);

        Assert.Null(await reader.GetAsync("pro", RateLimitCategory.H));
        Assert.Null(await reader.GetAsync("pro", RateLimitCategory.H)); // dentro del backoff
        Assert.Equal(1, handler.Calls); // no consulta Subscription en cada request

        cache.Expire(UnavailableKey); // pasan los 30 s
        var snapshot = await reader.GetAsync("pro", RateLimitCategory.H);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(5m, snapshot!.MultiplierOverride);
    }
}
