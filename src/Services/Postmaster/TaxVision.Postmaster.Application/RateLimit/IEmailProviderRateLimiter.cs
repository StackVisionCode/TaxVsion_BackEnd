using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.RateLimit;

/// <summary>Cupo consumido; si <see cref="Allowed"/> es false, <see cref="RetryAfter"/> sugiere cuándo reintentar.</summary>
public sealed record RateLimitDecision(bool Allowed, TimeSpan? RetryAfter);

/// <summary>
/// Partición explícita por <paramref name="stream"/> además de (providerCode, tenantId) — un envío
/// Bulk (campañas) nunca consume el mismo balde de cupo que Transactional, así que una ráfaga de
/// campaña no puede demorar un OTP o un email de recibo del mismo tenant detrás suyo.
/// </summary>
public interface IEmailProviderRateLimiter
{
    Task<RateLimitDecision> AcquireAsync(
        string providerCode,
        Guid tenantId,
        EmailStream stream,
        int limitPerMinute,
        CancellationToken ct = default
    );
}
