using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// GW-05 — la segunda mitad del arreglo. Aun con el histograma, agregar la ventana entera en cada
/// petición recorre un número fijo pero no trivial de buckets. Acá se calcula <b>una vez cada
/// <see cref="RefreshInterval"/></b> y el camino por petición pasa a ser una lectura de campo.
///
/// <para>
/// El desfase máximo entre la sobrecarga real y la decisión es el intervalo de refresco, irrelevante
/// contra una ventana de 60 segundos: la señal ya es un promedio de ese minuto, no un instante.
/// </para>
/// </summary>
public sealed class OverloadSignal(
    RequestOutcomeWindow window,
    IOptionsMonitor<LoadShedderOptions> options,
    ILogger<OverloadSignal> logger
)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    private volatile bool isOverloaded;
    private int wasOverloaded;

    public bool IsOverloaded => isOverloaded;

    /// <summary>Recalcula la señal. Lo llama <see cref="OverloadSignalRefresher"/>; los tests lo
    /// invocan directo para no depender del reloj.</summary>
    public void Refresh()
    {
        var current = options.CurrentValue;
        var snapshot = window.GetSnapshot();

        var overloaded =
            snapshot.SampleCount >= current.MinSamples
            && (
                snapshot.P99LatencyMs > current.P99LatencyThresholdMs
                || snapshot.ErrorRate5xx > current.ErrorRate5xxThreshold
            );

        isOverloaded = overloaded;

        if (!overloaded)
        {
            Interlocked.Exchange(ref wasOverloaded, 0);
            return;
        }

        // Edge-triggered: solo en la transición, no en cada refresco — si no, el log se llena de
        // líneas idénticas 5 veces por segundo justo durante el episodio.
        if (Interlocked.CompareExchange(ref wasOverloaded, 1, 0) != 0)
            return;

        GatewayMetrics.LoadSheddingActivated.Add(1);
        logger.LogWarning(
            "Load shedding activated: p99={P99LatencyMs}ms errorRate5xx={ErrorRate5xx:P1} samples={SampleCount}",
            snapshot.P99LatencyMs,
            snapshot.ErrorRate5xx,
            snapshot.SampleCount
        );
    }
}
