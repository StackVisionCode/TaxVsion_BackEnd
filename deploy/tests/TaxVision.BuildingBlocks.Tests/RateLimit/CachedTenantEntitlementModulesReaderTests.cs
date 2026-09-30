using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// A6/A5.6 — la caché del lector de módulos del gate. Lo que se protege acá no es el ahorro de
/// queries (eso es evidente) sino que la caché no aplaste la diferencia entre "no sé" y "no tiene
/// nada": la primera hace que el gate NO deniegue y la segunda que SÍ, y confundirlas le daría
/// acceso completo a un tenant vencido.
///
/// El doble de caché serializa a JSON con las mismas opciones que <c>RedisCacheService</c>, no
/// guarda el objeto tal cual: una caché en memoria devolvería la misma instancia y no probaría que
/// lo cacheado sobrevive el viaje a Redis.
/// </summary>
public sealed class CachedTenantEntitlementModulesReaderTests
{
    private readonly Guid tenantId = Guid.NewGuid();

    [Fact]
    public async Task First_call_reads_through_and_the_second_comes_from_the_cache()
    {
        var cache = new JsonFakeCacheService();
        var inner = new CountingModulesReader(["campaigns", "comms"]);
        var reader = new CachedTenantEntitlementModulesReader(cache, inner);

        var first = await reader.GetEnabledModulesAsync(tenantId);
        var second = await reader.GetEnabledModulesAsync(tenantId);

        Assert.Equal(["campaigns", "comms"], first);
        Assert.Equal(["campaigns", "comms"], second);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task A_tenant_without_projection_is_never_cached()
    {
        // `null` = el evento de entitlements todavía no llegó, y el gate no deniega por eso.
        // Cachearlo alargaría esa ventana de fail-open a un minuto por cada tenant recién creado.
        var cache = new JsonFakeCacheService();
        var inner = new CountingModulesReader(null);
        var reader = new CachedTenantEntitlementModulesReader(cache, inner);

        Assert.Null(await reader.GetEnabledModulesAsync(tenantId));
        Assert.Null(await reader.GetEnabledModulesAsync(tenantId));

        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task An_empty_list_IS_cached_and_comes_back_empty_not_null()
    {
        // El caso del tenant vencido, y el que un envoltorio mal hecho rompe: si la entrada cacheada
        // se leyera como "no hay entrada", el gate pasaría de denegar a fail-open en la segunda
        // petición — acceso completo a quien no pagó, y solo a partir del segundo clic.
        var cache = new JsonFakeCacheService();
        var inner = new CountingModulesReader([]);
        var reader = new CachedTenantEntitlementModulesReader(cache, inner);

        var first = await reader.GetEnabledModulesAsync(tenantId);
        var second = await reader.GetEnabledModulesAsync(tenantId);

        Assert.NotNull(first);
        Assert.Empty(first);
        Assert.NotNull(second);
        Assert.Empty(second);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Invalidating_makes_the_next_call_see_the_new_plan()
    {
        // El camino normal: el handler compartido de la proyección invalida justo después de guardar,
        // así que un upgrade se nota sin esperar el TTL.
        var cache = new JsonFakeCacheService();
        var inner = new CountingModulesReader([]);
        var reader = new CachedTenantEntitlementModulesReader(cache, inner);

        Assert.Empty((await reader.GetEnabledModulesAsync(tenantId))!);

        inner.Modules = ["campaigns"];
        await reader.InvalidateAsync(tenantId);

        Assert.Equal(["campaigns"], await reader.GetEnabledModulesAsync(tenantId));
    }

    [Fact]
    public async Task Each_tenant_has_its_own_entry()
    {
        var cache = new JsonFakeCacheService();
        var inner = new PerTenantModulesReader();
        var reader = new CachedTenantEntitlementModulesReader(cache, inner);

        var enterprise = Guid.NewGuid();
        var expired = Guid.NewGuid();
        inner.Modules[enterprise] = ["campaigns", "comms"];
        inner.Modules[expired] = [];

        Assert.Equal(["campaigns", "comms"], await reader.GetEnabledModulesAsync(enterprise));
        Assert.Empty((await reader.GetEnabledModulesAsync(expired))!);
        // Y otra vez, ya desde caché: la clave no se cruza.
        Assert.Equal(["campaigns", "comms"], await reader.GetEnabledModulesAsync(enterprise));
        Assert.Empty((await reader.GetEnabledModulesAsync(expired))!);
    }

    private sealed class CountingModulesReader(IReadOnlyList<string>? modules) : ITenantEntitlementModulesReader
    {
        public IReadOnlyList<string>? Modules { get; set; } = modules;
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Modules);
        }
    }

    private sealed class PerTenantModulesReader : ITenantEntitlementModulesReader
    {
        public Dictionary<Guid, IReadOnlyList<string>?> Modules { get; } = [];

        public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Modules.TryGetValue(tenantId, out var modules) ? modules : null);
    }

    /// <summary>Guarda bytes JSON con las opciones de <c>RedisCacheService</c>, no el objeto.</summary>
    private sealed class JsonFakeCacheService : ICacheService
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };

        private readonly Dictionary<string, byte[]> store = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(
                store.TryGetValue(key, out var bytes) ? JsonSerializer.Deserialize<T>(bytes, Options) : default
            );

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            store[key] = JsonSerializer.SerializeToUtf8Bytes(value, Options);
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
            var value = await factory(ct);
            await SetAsync(key, value, ttl, ct);
            return value;
        }
    }
}
