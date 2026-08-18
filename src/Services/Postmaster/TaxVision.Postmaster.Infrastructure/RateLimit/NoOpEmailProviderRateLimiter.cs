using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.RateLimit;

/// <summary>Usado cuando no hay Redis configurado (dev local) — nunca limita, degrada sin romper el envío.</summary>
public sealed class NoOpEmailProviderRateLimiter : IEmailProviderRateLimiter
{
    public Task<RateLimitDecision> AcquireAsync(
        string providerCode,
        Guid tenantId,
        EmailStream stream,
        int limitPerMinute,
        CancellationToken ct = default
    ) => Task.FromResult(new RateLimitDecision(true, null));
}
