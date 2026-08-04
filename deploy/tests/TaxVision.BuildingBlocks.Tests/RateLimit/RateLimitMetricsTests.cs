using System.Diagnostics.Metrics;
using BuildingBlocks.Infrastructure.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// RateLimit Fase 8 (Plan_Implementacion_Fases.md §8) — verifica que
/// <see cref="RateLimitMetrics"/> realmente emite vía <see cref="MeterListener"/> (mismo mecanismo
/// que usa un exporter OTel real), no solo que compila. Mismo patrón que
/// <c>AuthorizationMetricsTests</c> (RBAC Fase 10).
/// </summary>
[Collection(RateLimitMetricsCollection.Name)]
public sealed class RateLimitMetricsTests : IDisposable
{
    private readonly List<(string Instrument, long Value, IReadOnlyDictionary<string, string?> Tags)> _measurements =
    [];
    private readonly MeterListener _listener;

    public RateLimitMetricsTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RateLimitMetrics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, state) =>
            {
                var tagMap = tags.ToArray().ToDictionary(t => t.Key, t => t.Value?.ToString());
                _measurements.Add((instrument.Name, measurement, tagMap));
            }
        );
        _listener.Start();
    }

    [Fact]
    public void RecordEvaluated_emits_with_policy_layer_tenant_and_plan_tags()
    {
        using var metrics = new RateLimitMetrics();
        var tenantId = Guid.NewGuid();

        metrics.RecordEvaluated("customer.g.create", "user", tenantId, "pro");

        var measurement = Assert.Single(_measurements);
        Assert.Equal("ratelimit.evaluated_total", measurement.Instrument);
        Assert.Equal(1, measurement.Value);
        Assert.Equal("customer.g.create", measurement.Tags["policy"]);
        Assert.Equal("user", measurement.Tags["layer"]);
        Assert.Equal(tenantId.ToString("N"), measurement.Tags["tenant_id"]);
        Assert.Equal("pro", measurement.Tags["plan"]);
    }

    [Fact]
    public void RecordBlocked_emits_with_the_given_layer()
    {
        using var metrics = new RateLimitMetrics();

        metrics.RecordBlocked("customer.g.create", "tenant", Guid.NewGuid(), "starter");

        var measurement = Assert.Single(_measurements);
        Assert.Equal("ratelimit.blocked_total", measurement.Instrument);
        Assert.Equal("tenant", measurement.Tags["layer"]);
    }

    [Fact]
    public void RecordFallbackOpen_emits_with_policy_and_reason_only()
    {
        using var metrics = new RateLimitMetrics();

        metrics.RecordFallbackOpen("customer.g.create", "redis_primary");

        var measurement = Assert.Single(_measurements);
        Assert.Equal("ratelimit.fallback_open_total", measurement.Instrument);
        Assert.Equal("customer.g.create", measurement.Tags["policy"]);
        Assert.Equal("redis_primary", measurement.Tags["reason"]);
    }

    // Auditoria RateLimit hallazgo #6 — RateLimitAttribute llama esto cuando un endpoint
    // autenticado con [RateLimit] no puede resolver tenant_id/sub (fail-open silencioso antes de
    // esta fase). Distinto de RecordFallbackOpen: es una señal de configuración, no de infra caída.
    [Fact]
    public void RecordMissingClaims_emits_with_policy_only()
    {
        using var metrics = new RateLimitMetrics();

        metrics.RecordMissingClaims("customer.g.create");

        var measurement = Assert.Single(_measurements);
        Assert.Equal("ratelimit.missing_claims_total", measurement.Instrument);
        Assert.Equal(1, measurement.Value);
        Assert.Equal("customer.g.create", measurement.Tags["policy"]);
    }

    public void Dispose() => _listener.Dispose();
}
