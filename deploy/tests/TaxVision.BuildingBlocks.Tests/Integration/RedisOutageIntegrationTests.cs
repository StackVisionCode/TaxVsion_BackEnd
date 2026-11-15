using System.Diagnostics;
using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Integration;

/// <summary>
/// Caída de Redis con el mismo registro que los servicios, contra el Redis local. Redis "se cae" con un proxy
/// TCP, sin tocar el contenedor. Las claves llevan prefijo propio y se borran al terminar.
/// </summary>
public sealed class RedisOutageIntegrationTests : IAsyncLifetime
{
    private readonly string _key = $"bbchaos:{Guid.NewGuid():N}";
    private TcpChaosProxy _proxy = null!;
    private string _throughProxy = null!;

    public Task InitializeAsync()
    {
        var (host, port) = LocalBrokers.RedisEndpoint(LocalBrokers.RedisConnectionString);
        _proxy = new TcpChaosProxy(host, port);
        _throughProxy = LocalBrokers.ThroughProxy(LocalBrokers.RedisConnectionString, _proxy);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _proxy.DisposeAsync();
        await using var redis = await ConnectionMultiplexer.ConnectAsync(LocalBrokers.RedisConnectionString);
        await redis.GetDatabase().KeyDeleteAsync([new RedisKey($"taxvision:{_key}"), new RedisKey(_key)]);
    }

    // Con Redis caído la caché va al origen y no tumba la petición; cuando vuelve, guarda otra vez.
    [Fact(Timeout = 120_000)]
    public async Task The_cache_answers_from_the_source_while_redis_is_down_and_stores_again_when_it_returns()
    {
        _proxy.Cut();
        await using var services = Services();
        var cache = services.GetRequiredService<ICacheService>();

        Assert.Equal(1, await cache.GetOrCreateAsync(_key, _ => Task.FromResult(1)));
        var second = Stopwatch.StartNew();
        Assert.Equal(2, await cache.GetOrCreateAsync(_key, _ => Task.FromResult(2)));
        Assert.True(second.Elapsed < TimeSpan.FromSeconds(1), $"Con Redis caído tardó {second.Elapsed}.");

        _proxy.Restore();
        await LocalBrokers.WaitUntilAsync(
            async () =>
                await cache.GetOrCreateAsync(_key, _ => Task.FromResult(3)) == 3
                && await cache.GetOrCreateAsync(_key, _ => Task.FromResult(4)) == 3,
            TimeSpan.FromSeconds(60),
            "La caché no volvió a guardar después de volver Redis."
        );
    }

    // El servicio arranca sin Redis: el multiplexer existe, los comandos fallan al instante y se reconecta solo.
    [Fact(Timeout = 120_000)]
    public async Task Without_redis_the_service_starts_commands_fail_fast_and_it_reconnects_on_its_own()
    {
        _proxy.Cut();
        await using var services = Services();
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var counter = new RedisRateCounter(redis);
        var key = RateCounterKey.From(_key);

        Assert.False(redis.IsConnected);
        var failing = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<RedisException>(() => counter.IncrementAndGetAsync(key, TimeSpan.FromMinutes(1)));
        Assert.True(
            failing.Elapsed < TimeSpan.FromSeconds(1),
            $"Sin Redis el comando tardó {failing.Elapsed} en fallar."
        );

        _proxy.Restore();
        await LocalBrokers.WaitUntilAsync(() => redis.IsConnected, TimeSpan.FromSeconds(60), "No se reconectó solo.");
        Assert.Equal(1, await counter.IncrementAndGetAsync(key, TimeSpan.FromMinutes(1)));
    }

    // Redis se cae con el servicio corriendo y vuelve: sin reiniciar nada, todo sigue donde quedó.
    [Fact(Timeout = 120_000)]
    public async Task A_redis_drop_while_running_recovers_without_restarting()
    {
        await using var services = Services();
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var counter = new RedisRateCounter(redis);
        var key = RateCounterKey.From(_key);
        await LocalBrokers.WaitUntilAsync(() => redis.IsConnected, TimeSpan.FromSeconds(30), "No conectó al inicio.");
        Assert.Equal(1, await counter.IncrementAndGetAsync(key, TimeSpan.FromMinutes(1)));

        _proxy.Cut();
        await LocalBrokers.WaitUntilAsync(() => !redis.IsConnected, TimeSpan.FromSeconds(30), "No detectó el corte.");
        await Assert.ThrowsAnyAsync<RedisException>(() => counter.IncrementAndGetAsync(key, TimeSpan.FromMinutes(1)));

        _proxy.Restore();
        await LocalBrokers.WaitUntilAsync(() => redis.IsConnected, TimeSpan.FromSeconds(60), "No se reconectó solo.");
        Assert.Equal(2, await counter.IncrementAndGetAsync(key, TimeSpan.FromMinutes(1)));
    }

    private ServiceProvider Services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = _throughProxy })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisCache(configuration);
        services.AddTaxVisionRedis(_throughProxy);
        return services.BuildServiceProvider();
    }
}
