using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// GW-05. El p99 pasó de exacto (guardando cada muestra y ordenando) a aproximado por histograma
/// log-lineal. Eso es deliberado: el coste del exacto era ~1,92 MB por petición y un <c>Sort()</c> de
/// 4,3 millones de comparaciones bajo lock global, o sea que el shedder se caía justo bajo carga. Los
/// tests fijan la <b>cota del error</b>, no un valor exacto.
/// </summary>
public sealed class RequestOutcomeWindowTests
{
    /// <summary>Error relativo máximo del mapeo: 32 sub-buckets por octava.</summary>
    private const double MaxRelativeError = 1.0 / 32;

    [Fact]
    public void GetSnapshot_WithNoSamples_ReturnsZeroedSnapshot()
    {
        var snapshot = new RequestOutcomeWindow(60).GetSnapshot();

        Assert.Equal(0, snapshot.SampleCount);
        Assert.Equal(0, snapshot.P99LatencyMs);
        Assert.Equal(0, snapshot.ErrorRate5xx);
    }

    [Fact]
    public void GetSnapshot_ElP99SigueAlOutlier_DentroDeLaCotaDeError()
    {
        var window = new RequestOutcomeWindow(60);

        // 98 muestras normales + 1 outlier: con nearest-rank, el p99 de 99 muestras es la más alta.
        for (var i = 1; i <= 98; i++)
            window.Record(i, 200);
        window.Record(1000, 200);

        var snapshot = window.GetSnapshot();

        Assert.Equal(99, snapshot.SampleCount);
        Assert.InRange(snapshot.P99LatencyMs, 1000 * (1 - MaxRelativeError), 1000 * (1 + MaxRelativeError));
    }

    [Fact]
    public void GetSnapshot_UnaMinoriaLentaNoArrastraElP99()
    {
        var window = new RequestOutcomeWindow(60);

        // 995 rápidas + 5 lentas = 0,5%, por debajo del 1%: el p99 debe quedarse abajo. Es la
        // propiedad que impide que un puñado de peticiones lentas dispare el shedder.
        for (var i = 0; i < 995; i++)
            window.Record(20, 200);
        for (var i = 0; i < 5; i++)
            window.Record(9000, 200);

        Assert.InRange(window.GetSnapshot().P99LatencyMs, 0, 100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(500)]
    [InlineData(1999)]
    [InlineData(2000)]
    [InlineData(2001)]
    [InlineData(60000)]
    public void ElValorReconstruido_SiempreCaeDentroDeLaCotaDeError(long milliseconds)
    {
        var value = RequestOutcomeWindow.BucketValue(RequestOutcomeWindow.BucketIndex(milliseconds));

        // Absoluto de 1 ms para los buckets lineales de abajo, donde el relativo no aplica.
        Assert.InRange(value, milliseconds * (1 - MaxRelativeError) - 1, milliseconds * (1 + MaxRelativeError) + 1);
    }

    [Fact]
    public void ElMapeoDeBuckets_EsMonotono()
    {
        // Sin monotonía, recorrer los buckets en orden para el percentil daría un resultado
        // arbitrario: es la invariante de la que depende todo el cálculo.
        var previous = -1;
        for (long ms = 0; ms <= 70_000; ms++)
        {
            var index = RequestOutcomeWindow.BucketIndex(ms);
            Assert.True(index >= previous, $"BucketIndex({ms}) = {index} < {previous}");
            previous = index;
        }
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

        Assert.Equal(0, window.GetSnapshot().ErrorRate5xx);
    }

    [Fact]
    public void Record_EsSeguroBajoConcurrencia()
    {
        // El camino común ya no toma el lock global: lo que queda protegido es el reset del slot al
        // cambiar de segundo. Si ese reset perdiera incrementos, el total no cuadraría.
        var window = new RequestOutcomeWindow(60);

        Parallel.For(0, 10_000, i => window.Record(i % 500, i % 50 == 0 ? 503 : 200));

        var snapshot = window.GetSnapshot();
        Assert.Equal(10_000, snapshot.SampleCount);
        Assert.Equal(0.02, snapshot.ErrorRate5xx, precision: 5);
    }
}
