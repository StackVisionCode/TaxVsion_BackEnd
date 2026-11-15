using BuildingBlocks.Infrastructure.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Infrastructure;

public sealed class ServiceClientThrottlerTests
{
    /// <summary>
    /// Todos los servicios piden aquí su token M2M: si Redis cae, el grant sigue respondiendo. El freno es una
    /// defensa extra; la barrera principal es el Gateway, que no deja llegar el grant desde fuera.
    /// </summary>
    [Fact]
    public async Task Without_redis_the_grant_is_neither_blocked_nor_broken()
    {
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=300,asyncTimeout=300"
        );
        var throttler = new ServiceClientThrottler(
            unreachable,
            new RedisRateCounter(unreachable),
            NullLogger<ServiceClientThrottler>.Instance
        );

        Assert.Null(await throttler.GetRetryAfterAsync("10.0.0.5"));
        await throttler.RegisterFailureAsync("10.0.0.5");
    }

    [Fact]
    public async Task Without_an_address_nothing_is_counted()
    {
        await using var unreachable = await ConnectionMultiplexer.ConnectAsync(
            "127.0.0.1:1,abortConnect=false,connectTimeout=200"
        );
        var throttler = new ServiceClientThrottler(
            unreachable,
            new RedisRateCounter(unreachable),
            NullLogger<ServiceClientThrottler>.Instance
        );

        Assert.Null(await throttler.GetRetryAfterAsync(null));
        await throttler.RegisterFailureAsync("  ");
    }
}
