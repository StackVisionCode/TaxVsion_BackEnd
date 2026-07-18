using Microsoft.Extensions.Options;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.RateLimit;

namespace TaxVision.Connectors.Tests.RateLimit;

public class InMemoryProviderRateLimiterTests
{
    [Fact]
    public async Task WaitForSlotAsync_WithinLimit_DoesNotDelay()
    {
        var limiter = new InMemoryProviderRateLimiter(
            Options.Create(new ProviderRateLimiterOptions { MaxRequestsPerSecond = 10 })
        );

        var start = DateTime.UtcNow;
        await limiter.WaitForSlotAsync(ProviderCode.Gmail);
        var elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task WaitForSlotAsync_ExceedingWindowLimit_DelaysUntilNextSecond()
    {
        var limiter = new InMemoryProviderRateLimiter(
            Options.Create(new ProviderRateLimiterOptions { MaxRequestsPerSecond = 2 })
        );

        // Consume el budget del segundo actual.
        await limiter.WaitForSlotAsync(ProviderCode.Gmail);
        await limiter.WaitForSlotAsync(ProviderCode.Gmail);

        var start = DateTime.UtcNow;
        await limiter.WaitForSlotAsync(ProviderCode.Gmail);
        var elapsed = DateTime.UtcNow - start;

        // Debe haber esperado al próximo segundo — no instantáneo, pero acotado.
        Assert.True(elapsed > TimeSpan.FromMilliseconds(50));
        Assert.True(elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RecordRateLimitedAsync_ThenWaitForSlotAsync_WaitsOutCooldown()
    {
        var limiter = new InMemoryProviderRateLimiter(
            Options.Create(new ProviderRateLimiterOptions { MaxRequestsPerSecond = 100 })
        );

        await limiter.RecordRateLimitedAsync(ProviderCode.Graph, TimeSpan.FromMilliseconds(150));

        var start = DateTime.UtcNow;
        await limiter.WaitForSlotAsync(ProviderCode.Graph);
        var elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(120));
    }

    [Fact]
    public async Task RecordRateLimitedAsync_DoesNotAffectOtherProviders()
    {
        var limiter = new InMemoryProviderRateLimiter(
            Options.Create(new ProviderRateLimiterOptions { MaxRequestsPerSecond = 100 })
        );

        await limiter.RecordRateLimitedAsync(ProviderCode.Gmail, TimeSpan.FromSeconds(5));

        var start = DateTime.UtcNow;
        await limiter.WaitForSlotAsync(ProviderCode.Graph);
        var elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(200));
    }
}
