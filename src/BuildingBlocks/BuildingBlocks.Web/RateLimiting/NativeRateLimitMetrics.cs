using System.Diagnostics.Metrics;
using BuildingBlocks.Infrastructure.RateLimiting;

namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// 429 de los limiters nativos de ASP.NET Core (Gateway por IP, <c>auth-refresh</c>, Connectors,
/// webhooks…) y de los throttles de dominio que responden con el contrato común. El evaluador tiered
/// ya cuenta los suyos en <c>ratelimit.blocked_total</c>; los nativos no dejaban rastro en Grafana, y
/// son justo los que pegan a una oficina entera detrás de una IP. Mismo meter que
/// <see cref="RateLimitMetrics"/>, que <c>AddTaxVisionOpenTelemetry</c> exporta en todos los servicios.
/// </summary>
public static class NativeRateLimitMetrics
{
    public const string RejectedInstrumentName = "ratelimit.native_rejected_total";

    private static readonly Meter Meter = new(RateLimitMetrics.MeterName);

    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>(
        RejectedInstrumentName,
        description: "Requests rejected with 429 by a native (non-tiered) rate limiter, by policy"
    );

    public static void RecordRejected(string? policy) =>
        Rejected.Add(1, new KeyValuePair<string, object?>("policy", policy ?? "unknown"));
}
