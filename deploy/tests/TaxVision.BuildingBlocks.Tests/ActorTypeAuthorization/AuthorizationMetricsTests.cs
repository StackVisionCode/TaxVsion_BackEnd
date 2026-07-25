using System.Diagnostics.Metrics;
using BuildingBlocks.ActorTypeAuthorization;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// RBAC Fase 10 (RBAC_Hardening_Plan.md) — observabilidad. Captura las mediciones emitidas por el
/// <see cref="Counter{T}"/> vía <see cref="MeterListener"/> (mismo mecanismo que usa un exporter
/// OTel real) en vez de mockear el Meter — es la única forma de verificar que
/// <see cref="AuthorizationMetrics.RecordDecision"/> realmente emite con los tags correctos.
/// </summary>
[Collection(AuthorizationMetricsCollection.Name)]
public sealed class AuthorizationMetricsTests : IDisposable
{
    private readonly List<(long Value, string? Result, string? Layer)> _measurements = [];
    private readonly MeterListener _listener;

    public AuthorizationMetricsTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AuthorizationMetrics.MeterName && instrument.Name == "authz.decision")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, state) =>
            {
                string? result = null;
                string? layer = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "result")
                        result = tag.Value?.ToString();
                    else if (tag.Key == "layer")
                        layer = tag.Value?.ToString();
                }
                _measurements.Add((measurement, result, layer));
            }
        );
        _listener.Start();
    }

    [Fact]
    public void RecordDecision_emits_allow_with_the_given_layer()
    {
        using var metrics = new AuthorizationMetrics();

        metrics.RecordDecision(allowed: true, "1");

        var measurement = Assert.Single(_measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal("allow", measurement.Result);
        Assert.Equal("1", measurement.Layer);
    }

    [Fact]
    public void RecordDecision_emits_deny_with_the_given_layer()
    {
        using var metrics = new AuthorizationMetrics();

        metrics.RecordDecision(allowed: false, "2");

        var measurement = Assert.Single(_measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal("deny", measurement.Result);
        Assert.Equal("2", measurement.Layer);
    }

    [Fact]
    public void RecordDecision_tags_layer_3b_for_resource_ownership_decisions()
    {
        using var metrics = new AuthorizationMetrics();

        metrics.RecordDecision(allowed: true, "3b");

        var measurement = Assert.Single(_measurements);
        Assert.Equal("3b", measurement.Layer);
    }

    public void Dispose() => _listener.Dispose();
}
