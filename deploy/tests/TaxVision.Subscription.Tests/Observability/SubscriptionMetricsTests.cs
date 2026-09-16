using System.Diagnostics.Metrics;
using TaxVision.Subscription.Infrastructure.Observability;
using Xunit;

namespace TaxVision.Subscription.Tests.Observability;

/// <summary>Se escucha con <see cref="MeterListener"/> (el mismo mecanismo que un exporter OTel) para
/// comprobar que las mediciones salen de verdad, con su instrumento, valor y tag <c>add_on_code</c>.
/// El Meter lleva un nombre único por instancia para no cruzarse con otra clase en paralelo.</summary>
public sealed class SubscriptionMetricsTests : IDisposable
{
    private readonly string _meterName = $"TaxVision.Subscription.Tests.{Guid.NewGuid():N}";
    private readonly List<(string Instrument, long Value, string? Code)> _measurements = [];
    private readonly MeterListener _listener;

    public SubscriptionMetricsTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == _meterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags)
        );
        _listener.Start();
    }

    [Fact]
    public void Lifecycle_counters_emit_with_the_add_on_code_tag()
    {
        using var metrics = new SubscriptionMetrics(_meterName);

        metrics.RecordAddOnPurchased("email.addon");
        metrics.RecordAddOnAbsorbed("comms.addon");
        metrics.RecordAddOnExpired("reports.addon");

        Assert.Contains(
            _measurements,
            m => m.Instrument == "subscription.addons.purchased_total" && m.Code == "email.addon" && m.Value == 1
        );
        Assert.Contains(
            _measurements,
            m => m.Instrument == "subscription.addons.absorbed_total" && m.Code == "comms.addon" && m.Value == 1
        );
        Assert.Contains(
            _measurements,
            m => m.Instrument == "subscription.addons.expired_total" && m.Code == "reports.addon" && m.Value == 1
        );
    }

    [Fact]
    public void Billed_revenue_accumulates_the_amount_in_cents_per_add_on()
    {
        using var metrics = new SubscriptionMetrics(_meterName);

        metrics.RecordAddOnBilled("email.addon", 2900);

        var billed = Assert.Single(
            _measurements.FindAll(m => m.Instrument == "subscription.addons.billed_cents_total")
        );
        Assert.Equal(2900, billed.Value);
        Assert.Equal("email.addon", billed.Code);
    }

    private void Capture(string instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? code = null;
        foreach (var pair in tags)
        {
            if (pair.Key == "add_on_code")
                code = pair.Value?.ToString();
        }

        _measurements.Add((instrument, value, code));
    }

    public void Dispose() => _listener.Dispose();
}
