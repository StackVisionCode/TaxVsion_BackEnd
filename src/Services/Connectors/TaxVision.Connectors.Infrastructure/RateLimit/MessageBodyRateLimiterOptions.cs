namespace TaxVision.Connectors.Infrastructure.RateLimit;

public sealed class MessageBodyRateLimiterOptions
{
    public int MaxRequestsPerMinute { get; set; } = 10;
}
