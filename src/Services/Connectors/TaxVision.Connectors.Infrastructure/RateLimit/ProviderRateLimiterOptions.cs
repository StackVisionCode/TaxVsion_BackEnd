namespace TaxVision.Connectors.Infrastructure.RateLimit;

public sealed class ProviderRateLimiterOptions
{
    public int MaxRequestsPerSecond { get; set; } = 10;
}
