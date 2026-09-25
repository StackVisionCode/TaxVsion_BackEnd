using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// Los 429 de los limiters nativos (Gateway por IP, auth-refresh, Connectors…) no dejaban métrica:
/// en Grafana solo se veían los del evaluador tiered.
/// </summary>
[Collection(RateLimitMetricsCollection.Name)]
public sealed class NativeRateLimitMetricsTests : IDisposable
{
    private readonly List<(string Instrument, IReadOnlyDictionary<string, string?> Tags)> _measurements = [];
    private readonly MeterListener _listener;

    public NativeRateLimitMetricsTests()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RateLimitMetrics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) =>
                _measurements.Add((instrument.Name, tags.ToArray().ToDictionary(t => t.Key, t => t.Value?.ToString())))
        );
        _listener.Start();
    }

    [Fact]
    public async Task A_native_rejection_is_counted_with_its_policy()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Items[RateLimitRejection.PolicyItemKey] = "gateway.pre_auth_by_ip";

        await RateLimitRejection.OnRejected(
            new OnRejectedContext { HttpContext = context, Lease = new RejectedLease() },
            CancellationToken.None
        );

        var measurement = Assert.Single(
            _measurements,
            m => m.Instrument == NativeRateLimitMetrics.RejectedInstrumentName
        );
        Assert.Equal("gateway.pre_auth_by_ip", measurement.Tags["policy"]);
    }

    [Fact]
    public async Task The_tiered_path_is_not_counted_twice()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // El filtro tiered escribe con WriteAsync y ya cuenta en ratelimit.blocked_total.
        await RateLimitRejection.WriteAsync(context, 30, "customer.h.search", "user");

        Assert.DoesNotContain(_measurements, m => m.Instrument == NativeRateLimitMetrics.RejectedInstrumentName);
    }

    public void Dispose() => _listener.Dispose();

    private sealed class RejectedLease : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
