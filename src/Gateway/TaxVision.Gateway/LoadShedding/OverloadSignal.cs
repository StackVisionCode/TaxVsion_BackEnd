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
/// La señal tiene dos umbrales: se <b>activa</b> solo si la sobrecarga se sostiene
/// <see cref="LoadShedderOptions.ActivationSeconds"/> seguidos, y se <b>apaga</b> recién cuando p99 y
/// 5xx bajan a <see cref="LoadShedderOptions.RecoveryRatio"/> de sus umbrales. Así un pico aislado no
/// sheddea a la flota y un p99 que oscila alrededor del umbral no enciende y apaga en bucle.
/// </para>
/// </summary>
public sealed class OverloadSignal(
    RequestOutcomeWindow window,
    IOptionsMonitor<LoadShedderOptions> options,
    ILogger<OverloadSignal> logger,
    TimeProvider? timeProvider = null
)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Lock gate = new();
    private volatile bool isOverloaded;
    private DateTimeOffset? breachingSince;

    public bool IsOverloaded => isOverloaded;

    /// <summary>Recalcula la señal. Lo llama <see cref="OverloadSignalRefresher"/>; los tests lo
    /// invocan directo para no depender del reloj.</summary>
    public void Refresh()
    {
        var current = options.CurrentValue;
        var snapshot = window.GetSnapshot();
        var now = clock.GetUtcNow();

        lock (gate)
        {
            if (isOverloaded)
            {
                if (!IsRecovered(snapshot, current))
                    return;

                isOverloaded = false;
                breachingSince = null;
                logger.LogInformation(
                    "Load shedding deactivated: p99={P99LatencyMs}ms errorRate5xx={ErrorRate5xx:P1} samples={SampleCount}",
                    snapshot.P99LatencyMs,
                    snapshot.ErrorRate5xx,
                    snapshot.SampleCount
                );
                return;
            }

            if (!IsBreaching(snapshot, current))
            {
                breachingSince = null;
                return;
            }

            breachingSince ??= now;
            if (now - breachingSince.Value < TimeSpan.FromSeconds(current.ActivationSeconds))
                return;

            isOverloaded = true;
            GatewayMetrics.LoadSheddingActivated.Add(1);
            logger.LogWarning(
                "Load shedding activated: p99={P99LatencyMs}ms errorRate5xx={ErrorRate5xx:P1} samples={SampleCount} sustained={ActivationSeconds}s",
                snapshot.P99LatencyMs,
                snapshot.ErrorRate5xx,
                snapshot.SampleCount,
                current.ActivationSeconds
            );
        }
    }

    private static bool IsBreaching(WindowSnapshot snapshot, LoadShedderOptions current) =>
        snapshot.SampleCount >= current.MinSamples
        && (
            snapshot.P99LatencyMs > current.P99LatencyThresholdMs
            || snapshot.ErrorRate5xx > current.ErrorRate5xxThreshold
        );

    // Sin tráfico suficiente para afirmar sobrecarga, tampoco hay motivo para seguir descartando.
    private static bool IsRecovered(WindowSnapshot snapshot, LoadShedderOptions current)
    {
        if (snapshot.SampleCount < current.MinSamples)
            return true;

        var ratio = Math.Clamp(current.RecoveryRatio, 0.0, 1.0);
        return snapshot.P99LatencyMs <= current.P99LatencyThresholdMs * ratio
            && snapshot.ErrorRate5xx <= current.ErrorRate5xxThreshold * ratio;
    }
}
