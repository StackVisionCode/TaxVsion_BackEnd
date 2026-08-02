using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Gateway.Observability;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Implementación de referencia de <see cref="ILoadShedder"/> — Fase 5 del plan de rate limiting
/// (Plan_Implementacion_Fases.md), Capa 1 (load shedder global de flota) del modelo de 4 capas de
/// ADR-017. Sobrecarga = p99 de latencia propia del Gateway o tasa de 5xx por encima del umbral
/// configurado, medidos sobre <see cref="RequestOutcomeWindow"/>. Cuando hay sobrecarga, solo se
/// rechazan requests de los <see cref="LoadShedderOptions.TopConsumerCount"/> tenants de mayor
/// consumo en la ventana (<see cref="TenantConsumptionTracker"/>) — el resto del tráfico sigue
/// pasando, degradando gracefully en vez de tumbar la flota entera de un golpe.
/// </summary>
public sealed class LoadShedder(
    RequestOutcomeWindow window,
    TenantConsumptionTracker tenantTracker,
    IOptions<LoadShedderOptions> options,
    ILogger<LoadShedder> logger
) : ILoadShedder
{
    private readonly LoadShedderOptions options = options.Value;
    private int wasOverloaded;

    public int RetryAfterSeconds => options.RetryAfterSeconds;

    public bool ShouldShed(string tenantKey)
    {
        if (!options.Enabled)
            return false;

        var snapshot = window.GetSnapshot();
        if (snapshot.SampleCount < options.MinSamples)
            return false;

        var overloaded =
            snapshot.P99LatencyMs > options.P99LatencyThresholdMs
            || snapshot.ErrorRate5xx > options.ErrorRate5xxThreshold;

        var topConsumers = tenantTracker.GetTopConsumers(options.TopConsumerCount);

        if (!overloaded)
        {
            Interlocked.Exchange(ref wasOverloaded, 0);
            return false;
        }

        // Edge-triggered: solo loguea el top-10 en la transición hacia sobrecarga, no en cada
        // request rechazado — evita saturar los logs mientras dura el episodio.
        if (Interlocked.CompareExchange(ref wasOverloaded, 1, 0) == 0)
        {
            GatewayMetrics.LoadSheddingActivated.Add(1);
            logger.LogWarning(
                "Load shedding activated: p99={P99LatencyMs}ms errorRate5xx={ErrorRate5xx:P1} topConsumers={TopConsumers}",
                snapshot.P99LatencyMs,
                snapshot.ErrorRate5xx,
                string.Join(", ", topConsumers.Select(t => $"{t.TenantKey}={t.RequestCount}"))
            );
        }

        return topConsumers.Any(t => t.TenantKey == tenantKey);
    }
}
