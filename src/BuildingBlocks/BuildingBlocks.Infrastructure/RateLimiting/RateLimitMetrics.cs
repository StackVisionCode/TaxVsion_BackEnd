using System.Diagnostics.Metrics;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// Fase 8 del plan de rate limiting (Plan_Implementacion_Fases.md §8) — observabilidad del
/// invariante §3.5/§3.3. Meter fijo compartido por todos los servicios que llaman
/// <c>AddTieredRateLimiting()</c> (registrado incondicionalmente en
/// <c>OpenTelemetryRegistration.AddTaxVisionOpenTelemetry</c> — mismo criterio que
/// <c>AuthorizationMetrics</c>, salvo que acá SÍ se necesita un tag <c>tenant_id</c>: el propio
/// invariante §3.5 lo exige (<c>ratelimit.evaluated_total{policy,layer,tenant_id,plan}</c>) y las
/// Fase 8 dashboards (<c>RateLimit_ByTenant.json</c>, alerta de &gt;90% cuota sostenido) son
/// imposibles sin esa dimensión. Cardinalidad acotada por cantidad real de tenants (no de
/// requests/usuarios) — a diferencia de <c>userId</c>, que <c>AuthorizationMetrics</c> excluye a
/// propósito.
/// </summary>
public sealed class RateLimitMetrics : IDisposable
{
    public const string MeterName = "TaxVision.RateLimit";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _evaluated;
    private readonly Counter<long> _blocked;
    private readonly Counter<long> _fallbackOpen;

    public RateLimitMetrics()
    {
        _evaluated = _meter.CreateCounter<long>(
            "ratelimit.evaluated_total",
            description: "Rate limit checks performed, by layer"
        );
        _blocked = _meter.CreateCounter<long>(
            "ratelimit.blocked_total",
            description: "Requests rejected with 429, by layer"
        );
        _fallbackOpen = _meter.CreateCounter<long>(
            "ratelimit.fallback_open_total",
            description: "Fail-open events — Redis down or plan/quota unresolved (invariante §3.3/§3.5)"
        );
    }

    /// <param name="layer">"user" (capa primaria) o "tenant" (capa overlay).</param>
    public void RecordEvaluated(string policy, string layer, Guid tenantId, string plan) =>
        _evaluated.Add(
            1,
            new KeyValuePair<string, object?>("policy", policy),
            new KeyValuePair<string, object?>("layer", layer),
            new KeyValuePair<string, object?>("tenant_id", tenantId.ToString("N")),
            new KeyValuePair<string, object?>("plan", plan)
        );

    public void RecordBlocked(string policy, string layer, Guid tenantId, string plan) =>
        _blocked.Add(
            1,
            new KeyValuePair<string, object?>("policy", policy),
            new KeyValuePair<string, object?>("layer", layer),
            new KeyValuePair<string, object?>("tenant_id", tenantId.ToString("N")),
            new KeyValuePair<string, object?>("plan", plan)
        );

    /// <param name="reason">"redis_primary" | "redis_overlay" | "quota_unresolved".</param>
    public void RecordFallbackOpen(string policy, string reason) =>
        _fallbackOpen.Add(
            1,
            new KeyValuePair<string, object?>("policy", policy),
            new KeyValuePair<string, object?>("reason", reason)
        );

    public void Dispose() => _meter.Dispose();
}
