using BuildingBlocks.Infrastructure.RateLimit;
using StackExchange.Redis;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// Requiere Redis local — el repo no usa Testcontainers para clases Redis-backed (convención
/// existente, ver <c>RedisTokenReferenceStore</c>/rate limiters Family-A, ninguno con tests hoy).
/// Saltada en CI; correr manualmente con <c>docker run -p 6379:6379 redis</c>.
/// </summary>
public class RedisRateCounterTests
{
    private const string ConnectionString = "localhost:6379";

    [Fact(Skip = "requires local Redis")]
    public async Task IncrementAndGetAsync_AtomicallyIncrementsAndSetsExpiry_OnFirstCall()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        var counter = new RedisRateCounter(redis);
        var key = RateCounterKey.From($"test:rate-counter:{Guid.NewGuid():N}");

        var first = await counter.IncrementAndGetAsync(key, TimeSpan.FromSeconds(5));
        var second = await counter.IncrementAndGetAsync(key, TimeSpan.FromSeconds(5));

        Assert.Equal(1, first);
        Assert.Equal(2, second);

        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key.Value);
        Assert.NotNull(ttl);
        Assert.True(ttl <= TimeSpan.FromSeconds(5));
    }
}
