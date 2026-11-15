using BuildingBlocks.Infrastructure.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>Contra el Redis local (user-secrets de Auth.Api): el umbral por dirección y su espera real.</summary>
public sealed class ServiceClientThrottlerIntegrationTests
{
    private static string RedisConnectionString =>
        new ConfigurationBuilder()
            .AddUserSecrets("c9f518b8-3374-4abd-a7b6-f0b20cb0877f")
            .Build()
            .GetConnectionString("Redis")
        ?? "localhost:6379";

    [Fact]
    public async Task An_address_is_blocked_after_the_failure_threshold_and_others_are_not()
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
        var throttler = new ServiceClientThrottler(
            redis,
            new RedisRateCounter(redis),
            NullLogger<ServiceClientThrottler>.Instance
        );
        var attacker = $"203.0.113.{Random.Shared.Next(1, 250)}-{Guid.NewGuid():N}";
        var neighbour = $"198.51.100.7-{Guid.NewGuid():N}";

        for (var i = 1; i < ServiceClientThrottler.MaxFailuresPerAddress; i++)
            await throttler.RegisterFailureAsync(attacker);
        Assert.Null(await throttler.GetRetryAfterAsync(attacker));

        await throttler.RegisterFailureAsync(attacker);

        var wait = await throttler.GetRetryAfterAsync(attacker);
        Assert.NotNull(wait);
        Assert.InRange(wait.Value, TimeSpan.FromSeconds(1), ServiceClientThrottler.FailureWindow);
        Assert.Null(await throttler.GetRetryAfterAsync(neighbour));
    }
}
