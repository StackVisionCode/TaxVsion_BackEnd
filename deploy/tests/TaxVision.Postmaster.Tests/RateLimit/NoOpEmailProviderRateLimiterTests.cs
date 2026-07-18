using TaxVision.Postmaster.Infrastructure.RateLimit;

namespace TaxVision.Postmaster.Tests.RateLimit;

public sealed class NoOpEmailProviderRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_always_allows()
    {
        var limiter = new NoOpEmailProviderRateLimiter();

        var decision = await limiter.AcquireAsync(
            "system-smtp",
            Guid.NewGuid(),
            limitPerMinute: 1,
            CancellationToken.None
        );

        Assert.True(decision.Allowed);
        Assert.Null(decision.RetryAfter);
    }
}
