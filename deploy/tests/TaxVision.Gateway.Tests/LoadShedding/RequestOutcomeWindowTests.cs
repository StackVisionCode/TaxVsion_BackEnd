using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

public sealed class RequestOutcomeWindowTests
{
    [Fact]
    public void GetSnapshot_WithNoSamples_ReturnsZeroedSnapshot()
    {
        var window = new RequestOutcomeWindow(60);

        var snapshot = window.GetSnapshot();

        Assert.Equal(0, snapshot.SampleCount);
        Assert.Equal(0, snapshot.P99LatencyMs);
        Assert.Equal(0, snapshot.ErrorRate5xx);
    }

    [Fact]
    public void GetSnapshot_ComputesP99FromRecordedLatencies()
    {
        var window = new RequestOutcomeWindow(60);

        // 99 muestras normales (1..98ms) + 1 outlier de 1000ms = 99 total. Con el método de rango
        // más cercano (nearest-rank), p99 de 99 muestras cae exactamente en la muestra más alta.
        for (var i = 1; i <= 98; i++)
            window.Record(i, 200);
        window.Record(1000, 200);

        var snapshot = window.GetSnapshot();

        Assert.Equal(99, snapshot.SampleCount);
        Assert.Equal(1000, snapshot.P99LatencyMs);
    }

    [Fact]
    public void GetSnapshot_ComputesErrorRateFrom5xxResponses()
    {
        var window = new RequestOutcomeWindow(60);

        for (var i = 0; i < 8; i++)
            window.Record(10, 200);
        for (var i = 0; i < 2; i++)
            window.Record(10, 503);

        var snapshot = window.GetSnapshot();

        Assert.Equal(10, snapshot.SampleCount);
        Assert.Equal(0.2, snapshot.ErrorRate5xx, precision: 5);
    }

    [Fact]
    public void GetSnapshot_Does_Not_Count_4xx_As_Errors()
    {
        var window = new RequestOutcomeWindow(60);

        window.Record(10, 404);
        window.Record(10, 401);

        var snapshot = window.GetSnapshot();

        Assert.Equal(0, snapshot.ErrorRate5xx);
    }
}
