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

    /// <summary>
    /// Umbral del Nivel 2 (GW-14): se descarta al tenant cuyo consumo supera este multiplo de la
    /// media de tenants activos. Es continuo, no un top-N — si todos consumen parecido nadie lo
    /// supera y nadie se sheddea, sea cual sea el numero de tenants, que es el resultado correcto:
    /// si la sobrecarga viene de un downstream lento y no de un tenant abusivo, rechazar trafico
    /// solo agrega errores.
    /// </summary>
    public double FairShareExcessFactor { get; init; } = 2.0;

    /// <summary>Criticidad por primer segmento de ruta (ver RequestCriticalityClassifier).</summary>
    public Dictionary<string, RequestCriticality> Criticality { get; init; } = [];

    /// <summary>Criticidad de una ruta no declarada. Standard: ni se protege ni se sacrifica sola.</summary>
    public RequestCriticality DefaultCriticality { get; init; } = RequestCriticality.Standard;

    public int RetryAfterSeconds { get; init; } = 5;
}
