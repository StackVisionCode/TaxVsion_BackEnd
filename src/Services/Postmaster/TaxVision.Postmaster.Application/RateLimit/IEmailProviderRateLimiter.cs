namespace TaxVision.Postmaster.Application.RateLimit;

/// <summary>Cupo consumido; si <see cref="Allowed"/> es false, <see cref="RetryAfter"/> sugiere cuándo reintentar.</summary>
public sealed record RateLimitDecision(bool Allowed, TimeSpan? RetryAfter);

public interface IEmailProviderRateLimiter
{
    Task<RateLimitDecision> AcquireAsync(
        string providerCode,
        Guid tenantId,
        int limitPerMinute,
        CancellationToken ct = default
    );
}
