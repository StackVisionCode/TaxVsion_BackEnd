using System.Threading.RateLimiting;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// El FixedWindowRateLimiter de .NET decía "Retry-After: 60" aunque faltaran pocos segundos para que se
/// liberara el cupo (medido contra la flota: auth-refresh y el pre-auth del Gateway).
/// </summary>
[Collection(RateLimitMetricsCollection.Name)]
public sealed class AlignedFixedWindowRateLimiterTests
{
    private static readonly DateTimeOffset MinuteStart = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static AlignedFixedWindowRateLimiter Create(ManualTime time, int permits = 3) =>
        new(new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) }, time);

    [Fact]
    public void The_rejection_reports_what_is_left_of_the_window_not_the_whole_window()
    {
        var time = new ManualTime(MinuteStart.AddSeconds(48));
        var limiter = Create(time);
        for (var i = 0; i < 3; i++)
            Assert.True(limiter.AttemptAcquire().IsAcquired);

        using var rejected = limiter.AttemptAcquire();

        Assert.False(rejected.IsAcquired);
        Assert.True(rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(12), retryAfter);
    }

    [Fact]
    public void Rejections_do_not_consume_and_the_next_window_starts_full()
    {
        var time = new ManualTime(MinuteStart.AddSeconds(10));
        var limiter = Create(time);
        for (var i = 0; i < 3; i++)
            limiter.AttemptAcquire();
        for (var i = 0; i < 20; i++)
            Assert.False(limiter.AttemptAcquire().IsAcquired);

        time.Now = MinuteStart.AddMinutes(1);

        for (var i = 0; i < 3; i++)
            Assert.True(limiter.AttemptAcquire().IsAcquired);
        Assert.False(limiter.AttemptAcquire().IsAcquired);
    }

    [Fact]
    public void A_partition_that_used_nothing_in_the_current_window_is_idle()
    {
        var time = new ManualTime(MinuteStart.AddSeconds(5));
        var limiter = Create(time);
        limiter.AttemptAcquire();
        Assert.Null(limiter.IdleDuration);

        time.Now = MinuteStart.AddMinutes(2);

        Assert.NotNull(limiter.IdleDuration);
    }

    [Fact]
    public void Queueing_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() =>
            new AlignedFixedWindowRateLimiter(
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromSeconds(1),
                    QueueLimit = 5,
                }
            )
        );
    }

    [Fact]
    public async Task The_429_carries_the_real_wait_end_to_end()
    {
        var time = new ManualTime(MinuteStart.AddSeconds(53));
        var limiter = Create(time, permits: 1);
        limiter.AttemptAcquire();
        using var lease = limiter.AttemptAcquire();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await RateLimitRejection.OnRejected(
            new OnRejectedContext { HttpContext = context, Lease = lease },
            CancellationToken.None
        );

        Assert.Equal("7", context.Response.Headers.RetryAfter.ToString());
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override long GetTimestamp() => Now.UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
