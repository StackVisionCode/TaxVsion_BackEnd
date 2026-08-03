using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.RateLimit;

namespace TaxVision.Postmaster.Tests.RateLimit;

public sealed class NoOpEmailProviderRateLimiterTests
{
    [Theory]
    [InlineData(EmailStream.Transactional)]
    [InlineData(EmailStream.Bulk)]
    public async Task AcquireAsync_always_allows_regardless_of_stream(EmailStream stream)
    {
        var limiter = new NoOpEmailProviderRateLimiter();

        var decision = await limiter.AcquireAsync(
            "system-smtp",
            Guid.NewGuid(),
            stream,
            limitPerMinute: 1,
            CancellationToken.None
        );

        Assert.True(decision.Allowed);
        Assert.Null(decision.RetryAfter);
    }
}
