using BuildingBlocks.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Caching;

/// <summary>
/// BB-07. Los dos defectos corregidos son invisibles en un test de humo: el de tipos valor sólo
/// aparece con un <c>T</c> no-nullable, y la estampida sólo con concurrencia real.
/// </summary>
public sealed class RedisCacheServiceTests
{
    [Fact]
    public async Task GetOrCreateAsync_ConTipoValor_LlamaLaFactoryEnUnMiss()
    {
        // El bug: `cached is not null` sobre el default de un tipo valor. Para T = bool un miss
        // devuelve false, que no es null, así que se reportaba hit y la factory nunca corría —
        // el llamador recibía `false` en vez del valor real, para siempre.
        var cache = new RedisCacheService(new FakeDistributedCache());

        var result = await cache.GetOrCreateAsync("k", _ => Task.FromResult(true));

        Assert.True(result);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConNullCacheado_NoRecalcula()
    {
        // El reverso del mismo bug: un null legítimamente cacheado se leía como miss y la factory
        // volvía a correr en cada lectura.
        var inner = new FakeDistributedCache();
        var cache = new RedisCacheService(inner);
        var calls = 0;

        await cache.SetAsync<string?>("k", null);
        for (var i = 0; i < 3; i++)
        {
            await cache.GetOrCreateAsync<string?>(
                "k",
                _ =>
                {
                    calls++;
                    return Task.FromResult<string?>("recalculado");
                }
            );
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrenteSobreLaMismaClave_LlamaLaFactoryUnaSolaVez()
    {
        var cache = new RedisCacheService(new FakeDistributedCache());
        var gate = new TaskCompletionSource();
        var calls = 0;

        var racers = Enumerable
            .Range(0, 50)
            .Select(_ =>
                cache.GetOrCreateAsync(
                    "hot",
                    async _ =>
                    {
                        Interlocked.Increment(ref calls);
                        await gate.Task;
                        return "v";
                    }
                )
            )
            .ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(racers);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal("v", r));
    }

    [Fact]
    public async Task GetOrCreateAsync_SiLaFactoryFalla_NoDejaLaClaveEnvenenada()
    {
        var cache = new RedisCacheService(new FakeDistributedCache());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync<string>("k", _ => throw new InvalidOperationException("boom"))
        );

        // Si la task fallida quedara en el diccionario de in-flight, todo caller posterior heredaría
        // la excepción sin volver a intentarlo nunca.
        Assert.Equal("ok", await cache.GetOrCreateAsync("k", _ => Task.FromResult("ok")));
    }

    [Fact]
    public async Task GetAsync_SinValorAlmacenado_DevuelveElDefault()
    {
        var cache = new RedisCacheService(new FakeDistributedCache());

        Assert.Null(await cache.GetAsync<string>("ausente"));
        Assert.False(await cache.GetAsync<bool>("ausente"));
    }

    [Fact]
    public async Task RemoveAsync_DejaLaClaveComoMiss()
    {
        var cache = new RedisCacheService(new FakeDistributedCache());

        await cache.SetAsync("k", "v");
        await cache.RemoveAsync("k");

        Assert.Null(await cache.GetAsync<string>("k"));
    }

    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = [];

        public byte[]? Get(string key) => _store.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default
        )
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
