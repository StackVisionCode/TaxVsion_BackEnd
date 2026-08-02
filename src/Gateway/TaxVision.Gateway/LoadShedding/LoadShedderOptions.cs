namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Configuración de la Capa 1 (load shedder global de flota) — Fase 5 del plan de rate limiting
/// (Plan_Implementacion_Fases.md). Bindeada desde la sección "LoadShedding" de appsettings.
/// </summary>
public sealed class LoadShedderOptions
{
    public const string SectionName = "LoadShedding";

    public bool Enabled { get; init; } = true;

    /// <summary>Latencia p99 propia del Gateway (incluye el round-trip completo al cluster YARP de
    /// destino) por encima de la cual se considera sobrecarga.</summary>
    public int P99LatencyThresholdMs { get; init; } = 2000;

    /// <summary>Fracción de respuestas 5xx (0.0-1.0) por encima de la cual se considera sobrecarga.</summary>
    public double ErrorRate5xxThreshold { get; init; } = 0.5;

    /// <summary>Tamaño de la ventana deslizante (segundos) para p99/error-rate y para el ranking de
    /// consumo por tenant.</summary>
    public int WindowSeconds { get; init; } = 60;

    /// <summary>Muestras mínimas en la ventana antes de evaluar sobrecarga — evita disparar en frío
    /// con 1-2 requests lentos.</summary>
    public int MinSamples { get; init; } = 20;

    /// <summary>Cuántos tenants (los de mayor consumo en la ventana) se priorizan para shedding
    /// cuando hay sobrecarga. El resto de tráfico sigue pasando mientras dure la sobrecarga.</summary>
    public int TopConsumerCount { get; init; } = 10;

    public int RetryAfterSeconds { get; init; } = 5;
}
