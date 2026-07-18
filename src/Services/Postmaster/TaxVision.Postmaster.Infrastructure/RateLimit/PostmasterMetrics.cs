using System.Diagnostics.Metrics;

namespace TaxVision.Postmaster.Infrastructure.RateLimit;

/// <summary>
/// Meter propio del servicio — registrado en OTel vía <c>AddTaxVisionOpenTelemetry</c> (el nombre
/// coincide con el <c>serviceName</c> pasado en Program.cs: "postmaster-service").
/// </summary>
public static class PostmasterMetrics
{
    private static readonly Meter Meter = new("postmaster-service");

    public static readonly Counter<long> RateLimitHits = Meter.CreateCounter<long>("postmaster_rate_limit_hits_total");

    public static readonly Counter<long> CircuitBreakerOpened = Meter.CreateCounter<long>(
        "postmaster_circuit_breaker_opened_total"
    );
}
