namespace TaxVision.Connectors.Infrastructure.RateLimit;

public sealed class AttachmentRateLimiterOptions
{
    public int MaxRequestsPerMinute { get; set; } = 5;
}
