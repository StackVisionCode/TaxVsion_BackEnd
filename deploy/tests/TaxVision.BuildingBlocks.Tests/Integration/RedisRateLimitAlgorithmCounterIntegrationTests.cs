using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using StackExchange.Redis;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// Ejecuta los scripts Lua de <see cref="RedisRateLimitAlgorithmCounter"/> contra un Redis real
/// (localhost:6379; fuera de los gates por el namespace Integration). Los fakes no pueden probar lo que
/// importa acá: que un rechazo no consuma cupo y que la espera informada sea la real.
/// </summary>
public sealed class RedisRateLimitAlgorithmCounterIntegrationTests : IAsyncLifetime
{
    private ConnectionMultiplexer redis = null!;
    private readonly List<string> keys = [];

    public async Task InitializeAsync() => redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");

    public async Task DisposeAsync()
    {
        var db = redis.GetDatabase();
        foreach (var key in keys)
            await db.KeyDeleteAsync(key);
        await redis.DisposeAsync();
    }

    private RateCounterKey NewKey()
    {
        var key = $"bbtest:rl:bbtest.x.algorithm:{Guid.NewGuid():N}";
        keys.Add(key);
        return RateCounterKey.From(key);
    }

    private RedisRateLimitAlgorithmCounter Counter() => new(redis);

    [Fact]
    public async Task VentanaFija_LosRechazosNoConsumenYLaEsperaEsElTtlRestante()
    {
        var key = NewKey();
        var window = TimeSpan.FromSeconds(3);

        Assert.False((await Counter().EvaluateAsync(key, RateLimitAlgorithm.FixedWindow, 2, window)).Exceeded);
        Assert.False((await Counter().EvaluateAsync(key, RateLimitAlgorithm.FixedWindow, 2, window)).Exceeded);

        RateLimitCounterResult rejected = default;
        for (var i = 0; i < 4; i++)
            rejected = await Counter().EvaluateAsync(key, RateLimitAlgorithm.FixedWindow, 2, window);

        Assert.True(rejected.Exceeded);
        Assert.InRange(rejected.RetryAfter, TimeSpan.FromMilliseconds(1), window);
        Assert.Equal(2, (long)await redis.GetDatabase().StringGetAsync(key.Value)); // los 4 rechazos no contaron
    }

    [Fact]
    public async Task VentanaFija_UnaClaveLegacySinTtlSeReexpiraEnVezDeBloquearParaSiempre()
    {
        var key = NewKey();
        await redis.GetDatabase().StringSetAsync(key.Value, 50); // sin TTL, por encima del límite

        var result = await Counter().EvaluateAsync(key, RateLimitAlgorithm.FixedWindow, 2, TimeSpan.FromSeconds(5));

        Assert.True(result.Exceeded);
        Assert.NotNull(await redis.GetDatabase().KeyTimeToLiveAsync(key.Value));
    }

    [Fact]
    public async Task VentanaDeslizante_LosRechazosNoEntranAlLogYLaEsperaEsElVencimientoDelMasViejo()
    {
        var key = NewKey();
        var window = TimeSpan.FromMilliseconds(1500);

        await Counter().EvaluateAsync(key, RateLimitAlgorithm.SlidingWindow, 2, window);
        await Counter().EvaluateAsync(key, RateLimitAlgorithm.SlidingWindow, 2, window);

        RateLimitCounterResult rejected = default;
        for (var i = 0; i < 3; i++)
            rejected = await Counter().EvaluateAsync(key, RateLimitAlgorithm.SlidingWindow, 2, window);

        Assert.True(rejected.Exceeded);
        Assert.InRange(rejected.RetryAfter, TimeSpan.FromMilliseconds(1), window);
        Assert.Equal(2, await redis.GetDatabase().SortedSetLengthAsync(key.Value));

        await Task.Delay(rejected.RetryAfter + TimeSpan.FromMilliseconds(100));
        Assert.False((await Counter().EvaluateAsync(key, RateLimitAlgorithm.SlidingWindow, 2, window)).Exceeded);
    }

    [Fact]
    public async Task CambiarElAlgoritmoDeUnaPolitica_NoRompeConWrongType()
    {
        // Lo que pasa en el deploy que mueve las búsquedas (H) de ventana deslizante a token bucket:
        // la misma clave pasa de sorted set a hash.
        var key = NewKey();
        await Counter().EvaluateAsync(key, RateLimitAlgorithm.SlidingWindow, 5, TimeSpan.FromSeconds(60));

        var asTokenBucket = await Counter()
            .EvaluateAsync(key, RateLimitAlgorithm.TokenBucket, 5, TimeSpan.FromSeconds(60));
        var asFixedWindow = await Counter()
            .EvaluateAsync(key, RateLimitAlgorithm.FixedWindow, 5, TimeSpan.FromSeconds(60));

        Assert.False(asTokenBucket.Exceeded);
        Assert.False(asFixedWindow.Exceeded);
        Assert.Equal(RedisType.String, await redis.GetDatabase().KeyTypeAsync(key.Value));
    }

    [Fact]
    public async Task TokenBucket_LaEsperaEsElTiempoHastaElProximoToken()
    {
        var key = NewKey();
        var window = TimeSpan.FromSeconds(1); // 2 tokens por segundo → 1 token cada 500 ms

        await Counter().EvaluateAsync(key, RateLimitAlgorithm.TokenBucket, 2, window);
        await Counter().EvaluateAsync(key, RateLimitAlgorithm.TokenBucket, 2, window);
        var rejected = await Counter().EvaluateAsync(key, RateLimitAlgorithm.TokenBucket, 2, window);

        Assert.True(rejected.Exceeded);
        Assert.InRange(rejected.RetryAfter, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(500));
    }
}
