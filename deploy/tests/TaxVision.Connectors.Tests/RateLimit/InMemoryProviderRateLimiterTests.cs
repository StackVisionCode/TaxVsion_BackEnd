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
        await limiter.WaitForSlotAsync(ProviderCode.Gmail, Guid.NewGuid());
        var elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task WaitForSlotAsync_ExceedingWindowLimit_DelaysUntilNextSecond()
    {
        var limiter = new InMemoryProviderRateLimiter(
            Options.Create(new ProviderRateLimiterOptions { MaxRequestsPerSecond = 2 })
        );

        // Arrancar al filo de un segundo: si no, las dos primeras llamadas pueden caer en segundos
        // distintos y no agotar el budget de ninguno.
        await Task.Delay(1000 - DateTimeOffset.UtcNow.Millisecond + 5);

        // Consume el budget del segundo actual.
        await limiter.WaitForSlotAsync(ProviderCode.Gmail, Guid.NewGuid());
        await limiter.WaitForSlotAsync(ProviderCode.Gmail, Guid.NewGuid());

        var startSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var start = DateTime.UtcNow;
        await limiter.WaitForSlotAsync(ProviderCode.Gmail, Guid.NewGuid());
        var elapsed = DateTime.UtcNow - start;

        // La propiedad real del limiter es cruzar al siguiente segundo de reloj, no tardar N ms: la
        // espera es `1000 - msIntoSecond`, que legítimamente puede ser de pocos ms.
        Assert.True(DateTimeOffset.UtcNow.ToUnixTimeSeconds() > startSecond);
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
        await limiter.WaitForSlotAsync(ProviderCode.Graph, Guid.NewGuid());
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
        await limiter.WaitForSlotAsync(ProviderCode.Graph, Guid.NewGuid());
        var elapsed = DateTime.UtcNow - start;

        Assert.True(elapsed < TimeSpan.FromMilliseconds(200));
    }
}
