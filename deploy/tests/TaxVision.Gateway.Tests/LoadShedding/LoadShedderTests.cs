using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// GW-14. Cada caso mapea a un modo de fallo real, no a cobertura por cobertura. El que da nombre al
/// hallazgo es <see cref="UnSoloTenantEnSobrecarga_NoSeSheddeaNada"/>: con el criterio anterior
/// (<c>GetTopConsumers(10)</c> sin piso) ese escenario rechazaba el <b>100%</b> del tráfico.
/// </summary>
public sealed class LoadShedderTests
{
    private const string StandardPath = "/customers/list";
    private const string BackgroundPath = "/growth/reports";
    private const string CriticalPath = "/payments-app/charge";

    private static LoadShedder CreateShedder(
        RequestOutcomeWindow window,
        TenantConsumptionTracker tracker,
        LoadShedderOptions? options = null
    )
    {
        var monitor = new StaticOptionsMonitor(options ?? Overloadable());

        // La senal se refresca a mano: en produccion lo hace OverloadSignalRefresher cada 200 ms, y
        // atarlo al reloj aqui haria los tests lentos y no deterministas (GW-05).
        var signal = new OverloadSignal(window, monitor, NullLogger<OverloadSignal>.Instance);
        signal.Refresh();

        return new LoadShedder(signal, tracker, new RequestCriticalityClassifier(monitor), monitor);
    }

    private static LoadShedderOptions Overloadable() =>
        new()
        {
            MinSamples = 1,
            FairShareExcessFactor = 2.0,
            Criticality = new Dictionary<string, RequestCriticality>
            {
                ["customers"] = RequestCriticality.Standard,
                ["growth"] = RequestCriticality.Background,
                ["payments-app"] = RequestCriticality.Critical,
            },
        };

    /// <summary>Ventana saturada: latencia por encima del umbral en todas las muestras.</summary>
    private static RequestOutcomeWindow OverloadedWindow()
    {
        var window = new RequestOutcomeWindow(60);
        for (var i = 0; i < 10; i++)
            window.Record(5000, 200);
        return window;
    }

    private static TenantConsumptionTracker TrackerWith(params (string Tenant, int Requests)[] consumption)
    {
        var tracker = new TenantConsumptionTracker(60);
        foreach (var (tenant, requests) in consumption)
        {
            for (var i = 0; i < requests; i++)
                tracker.RecordRequest(tenant);
        }

        return tracker;
    }

    [Fact]
    public void UnSoloTenantEnSobrecarga_NoSeSheddeaNada()
    {
        // El caso que daba corte total: con 1 tenant activo, ese tenant era todo el "top 10".
        var tracker = TrackerWith(("tenant-a", 100));
        var shedder = CreateShedder(OverloadedWindow(), tracker);

        Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate("tenant-a", StandardPath, false));
    }

    [Fact]
    public void TresTenantsConConsumoIdentico_NoSeSheddeaNinguno()
    {
        var tracker = TrackerWith(("a", 50), ("b", 50), ("c", 50));
        var shedder = CreateShedder(OverloadedWindow(), tracker);

        foreach (var tenant in new[] { "a", "b", "c" })
            Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate(tenant, StandardPath, false));
    }

    [Fact]
    public void UnOutlierEntreTresParejos_SoloSeSheddeaElOutlier()
    {
        var tracker = TrackerWith(("a", 10), ("b", 10), ("c", 10), ("abusivo", 150));
        var shedder = CreateShedder(OverloadedWindow(), tracker);

        Assert.Equal(SheddingVerdict.FairShareExcess, shedder.Evaluate("abusivo", StandardPath, false));
        foreach (var tenant in new[] { "a", "b", "c" })
            Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate(tenant, StandardPath, false));
    }

    [Fact]
    public void ConVeinteTenants_LaDecisionNoDependeDeCuantosSon()
    {
        var consumption = Enumerable.Range(0, 20).Select(i => ($"t{i}", 10)).Append(("abusivo", 90)).ToArray();
        var shedder = CreateShedder(OverloadedWindow(), TrackerWith(consumption));

        Assert.Equal(SheddingVerdict.FairShareExcess, shedder.Evaluate("abusivo", StandardPath, false));
        Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate("t0", StandardPath, false));
    }

    [Fact]
    public void LaCriticidadManda_SobreLaParteJusta()
    {
        // Background cae aunque el tenant esté por debajo de la media; Critical sobrevive aunque esté
        // muy por encima. Confirma que el orden de los niveles es el declarado.
        var tracker = TrackerWith(("modesto", 5), ("abusivo", 200), ("x", 5));
        var shedder = CreateShedder(OverloadedWindow(), tracker);

        Assert.Equal(SheddingVerdict.LowCriticality, shedder.Evaluate("modesto", BackgroundPath, false));
        Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate("abusivo", CriticalPath, false));
    }

    [Fact]
    public void ClienteDesconectado_SeDescartaSinMirarLosDemasNiveles()
    {
        // Sin sobrecarga y con una ruta Critical: si el veredicto sigue siendo Abandoned, el nivel 0
        // se evalúa primero, que es lo que lo hace útil.
        var shedder = CreateShedder(new RequestOutcomeWindow(60), TrackerWith(("a", 1)));

        Assert.Equal(SheddingVerdict.Abandoned, shedder.Evaluate("a", CriticalPath, true));
    }

    [Fact]
    public void SinSobrecarga_NoSeSheddeaAunqueElTenantSeaUnOutlier()
    {
        var window = new RequestOutcomeWindow(60);
        for (var i = 0; i < 10; i++)
            window.Record(10, 200);

        var shedder = CreateShedder(window, TrackerWith(("a", 10), ("abusivo", 500)));

        Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate("abusivo", StandardPath, false));
    }

    [Fact]
    public void PorDebajoDeMinSamples_NoSeEvaluaSobrecarga()
    {
        var window = new RequestOutcomeWindow(60);
        window.Record(9999, 503);

        var options = Overloadable();
        var shedder = CreateShedder(
            window,
            TrackerWith(("a", 1), ("abusivo", 500)),
            new LoadShedderOptions
            {
                MinSamples = 20,
                FairShareExcessFactor = options.FairShareExcessFactor,
                Criticality = options.Criticality,
            }
        );

        Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate("abusivo", StandardPath, false));
    }

    [Fact]
    public void Deshabilitado_NuncaSheddea()
    {
        var options = Overloadable();
        var shedder = CreateShedder(
            OverloadedWindow(),
            TrackerWith(("a", 1), ("abusivo", 500)),
            new LoadShedderOptions
            {
                Enabled = false,
                MinSamples = 1,
                Criticality = options.Criticality,
            }
        );

        Assert.Equal(SheddingVerdict.Allowed, shedder.Evaluate("abusivo", BackgroundPath, false));
    }

    [Fact]
    public void TasaDe5xx_DisparaSobrecargaAunqueLaLatenciaSeaBaja()
    {
        var window = new RequestOutcomeWindow(60);
        for (var i = 0; i < 10; i++)
            window.Record(10, 503);

        var shedder = CreateShedder(window, TrackerWith(("a", 1)));

        Assert.Equal(SheddingVerdict.LowCriticality, shedder.Evaluate("a", BackgroundPath, false));
    }

    [Theory]
    [InlineData("/customers/list", "customers")]
    [InlineData("/customers", "customers")]
    [InlineData("/PAYMENTS-APP/charge", "payments-app")]
    [InlineData("/", null)]
    [InlineData("", null)]
    public void FirstSegment_NormalizaLaClaveDeCriticidad(string path, string? expected)
    {
        Assert.Equal(expected, RequestCriticalityClassifier.FirstSegment(new PathString(path)));
    }

    private sealed class StaticOptionsMonitor(LoadShedderOptions value) : IOptionsMonitor<LoadShedderOptions>
    {
        public LoadShedderOptions CurrentValue => value;

        public LoadShedderOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<LoadShedderOptions, string?> listener) => null;
    }
}
