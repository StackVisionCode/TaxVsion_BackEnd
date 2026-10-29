using System.Diagnostics.Metrics;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Infrastructure.ClientForms.Security;
using TaxVision.Tasks.Infrastructure.Observability;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// Gauge y health check del censo de versiones de clave (X-9.3). El Meter va con nombre propio para no
/// escuchar las mediciones de otra suite; el health check se construye directo sobre el snapshot.
/// </summary>
public sealed class ClientFormsKeyVersionObservabilityTests
{
    [Fact]
    public void The_gauge_emits_one_row_count_per_key_version_without_a_tenant_tag()
    {
        var meterName = $"TaxVision.Tasks.ClientForms.Tests.{Guid.NewGuid():N}";
        var measurements = new List<(long Value, string? KeyVersion)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "client_forms.ciphertext_rows")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (_, value, tags, _) =>
            {
                string? version = null;
                foreach (var tag in tags)
                    if (tag.Key == "key_version")
                        version = tag.Value?.ToString();
                measurements.Add((value, version));
            }
        );
        listener.Start();

        using var metrics = new ClientFormsMetrics(meterName);
        metrics.UpdateCiphertextVersions(new Dictionary<short, long> { [1] = 5, [2] = 3 });
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, m => m is { Value: 5, KeyVersion: "1" });
        Assert.Contains(measurements, m => m is { Value: 3, KeyVersion: "2" });
    }

    [Fact]
    public void The_gauge_emits_nothing_before_the_first_census()
    {
        var meterName = $"TaxVision.Tasks.ClientForms.Tests.{Guid.NewGuid():N}";
        var seen = false;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName && instrument.Name == "client_forms.ciphertext_rows")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => seen = true);
        listener.Start();

        using var metrics = new ClientFormsMetrics(meterName);
        listener.RecordObservableInstruments();

        Assert.False(seen);
    }

    [Fact]
    public async Task Health_is_healthy_before_any_census()
    {
        var result = await Check(snapshot: null, current: 2, previous: 1);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Health_is_healthy_when_everything_is_on_the_current_version()
    {
        var result = await Check(new Dictionary<short, long> { [2] = 10 }, current: 2, previous: 1);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Health_is_degraded_while_rows_remain_on_the_previous_version()
    {
        var result = await Check(new Dictionary<short, long> { [1] = 4, [2] = 10 }, current: 2, previous: 1);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Health_is_unhealthy_when_a_retired_version_still_has_rows()
    {
        // v0 no es ni la actual (2) ni la anterior (1): su clave ya no está configurada, es ilegible.
        var result = await Check(new Dictionary<short, long> { [0] = 1, [2] = 10 }, current: 2, previous: 1);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task A_version_with_zero_rows_does_not_affect_health()
    {
        var result = await Check(new Dictionary<short, long> { [0] = 0, [2] = 10 }, current: 2, previous: 1);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static async Task<HealthCheckResult> Check(
        IReadOnlyDictionary<short, long>? snapshot,
        short current,
        short? previous
    )
    {
        var metrics = new ClientFormsMetrics($"TaxVision.Tasks.ClientForms.Tests.{Guid.NewGuid():N}");
        if (snapshot is not null)
            metrics.UpdateCiphertextVersions(snapshot);

        var options = Options.Create(
            new RotatingSecretProtectionOptions { MasterKeyVersion = current, PreviousMasterKeyVersion = previous }
        );
        var check = new ClientFormsKeyVersionsHealthCheck(metrics, options);
        return await check.CheckHealthAsync(new HealthCheckContext());
    }
}
