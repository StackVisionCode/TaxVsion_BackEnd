using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// La señal pasó de "umbral superado en este refresco" a dos umbrales: se activa solo si la sobrecarga
/// se sostiene <c>ActivationSeconds</c> y se apaga recién bajo <c>RecoveryRatio</c> del umbral. Con el
/// criterio anterior un request lento aislado activaba el shedding de toda la flota, y un p99 oscilando
/// alrededor del umbral lo encendía y apagaba varias veces por minuto.
/// </summary>
public sealed class OverloadSignalTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private static OverloadSignal CreateSignal(
        RequestOutcomeWindow window,
        ManualClock clock,
        LoadShedderOptions options
    ) => new(window, new StaticOptionsMonitor<LoadShedderOptions>(options), NullLogger<OverloadSignal>.Instance, clock);

    private static LoadShedderOptions Options(int activationSeconds = 10) =>
        new()
        {
            MinSamples = 10,
            P99LatencyThresholdMs = 1000,
            ActivationSeconds = activationSeconds,
            RecoveryRatio = 0.8,
        };

    private static void Record(RequestOutcomeWindow window, int count, double latencyMs)
    {
        for (var i = 0; i < count; i++)
            window.Record(latencyMs, 200);
    }

    [Fact]
    public void SobrecargaSostenida_SeActivaAlCumplirActivationSeconds()
    {
        var window = new RequestOutcomeWindow(60);
        Record(window, 20, 1500);
        var clock = new ManualClock();
        var signal = CreateSignal(window, clock, Options(activationSeconds: 10));

        signal.Refresh();
        Assert.False(signal.IsOverloaded);

        clock.Advance(TimeSpan.FromSeconds(9));
        signal.Refresh();
        Assert.False(signal.IsOverloaded);

        clock.Advance(TimeSpan.FromSeconds(1));
        signal.Refresh();
        Assert.True(signal.IsOverloaded);
    }

    [Fact]
    public void PicoQueSeCortaAntesDeTiempo_NoActivaYElContadorSeReinicia()
    {
        var window = new RequestOutcomeWindow(60);
        Record(window, 10, 1500);
        var clock = new ManualClock();
        var signal = CreateSignal(window, clock, Options(activationSeconds: 10));

        signal.Refresh(); // empieza a contar

        // Suficientes requests rápidos para que los lentos queden por debajo del 1% → p99 sano.
        clock.Advance(TimeSpan.FromSeconds(5));
        Record(window, 2000, 50);
        signal.Refresh();
        Assert.False(signal.IsOverloaded);

        // Vuelve a superar el umbral: hay que sostenerlo 10 s completos otra vez, no los 5 que faltaban.
        Record(window, 200, 1500);
        clock.Advance(TimeSpan.FromSeconds(1));
        signal.Refresh();
        clock.Advance(TimeSpan.FromSeconds(9));
        signal.Refresh();
        Assert.False(signal.IsOverloaded);

        clock.Advance(TimeSpan.FromSeconds(1));
        signal.Refresh();
        Assert.True(signal.IsOverloaded);
    }

    [Fact]
    public void ActivationSecondsCero_ActivaEnElPrimerRefresco()
    {
        var window = new RequestOutcomeWindow(60);
        Record(window, 20, 1500);
        var signal = CreateSignal(window, new ManualClock(), Options(activationSeconds: 0));

        signal.Refresh();

        Assert.True(signal.IsOverloaded);
    }

    [Fact]
    public void Histeresis_SigueActivoEntreLaBandaYElUmbral_YSeApagaDebajoDeLaBanda()
    {
        var window = new RequestOutcomeWindow(60);
        Record(window, 10, 1500);
        var signal = CreateSignal(window, new ManualClock(), Options(activationSeconds: 0));
        signal.Refresh();
        Assert.True(signal.IsOverloaded);

        // p99 ≈ 900 ms: ya no supera el umbral (1000) pero sigue por encima de la banda (800).
        Record(window, 990, 900);
        signal.Refresh();
        Assert.True(signal.IsOverloaded);

        // p99 ≈ 100 ms: por debajo de la banda → se apaga.
        Record(window, 99_000, 100);
        signal.Refresh();
        Assert.False(signal.IsOverloaded);
    }

    [Fact]
    public void PorDebajoDeMinSamples_NoSeActivaAunqueSeaLento()
    {
        var window = new RequestOutcomeWindow(60);
        Record(window, 5, 60_000);
        var signal = CreateSignal(window, new ManualClock(), Options(activationSeconds: 0));

        signal.Refresh();

        Assert.False(signal.IsOverloaded);
    }
}
