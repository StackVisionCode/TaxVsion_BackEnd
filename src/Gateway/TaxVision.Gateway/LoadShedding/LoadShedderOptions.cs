namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Configuración de la Capa 1 (load shedder global de flota) — Fase 5 del plan de rate limiting
/// (Plan_Implementacion_Fases.md). Bindeada desde la sección "LoadShedding" de appsettings.
/// </summary>
public sealed class LoadShedderOptions
{
    public const string SectionName = "LoadShedding";

    public bool Enabled { get; init; } = true;

    /// <summary>Latencia p99 del servidor (hasta que arranca la respuesta, incluye el round-trip al
    /// cluster YARP de destino) por encima de la cual se considera sobrecarga.</summary>
    public int P99LatencyThresholdMs { get; init; } = 5000;

    /// <summary>Fracción de respuestas 5xx (0.0-1.0) por encima de la cual se considera sobrecarga.</summary>
    public double ErrorRate5xxThreshold { get; init; } = 0.5;

    /// <summary>Tamaño de la ventana deslizante (segundos) para p99/error-rate y para el ranking de
    /// consumo por tenant.</summary>
    public int WindowSeconds { get; init; } = 60;

    /// <summary>
    /// Muestras mínimas en la ventana antes de evaluar sobrecarga. Con pocas muestras el p99 es en la
    /// práctica el request más lento (con N &lt; 100 es literalmente el máximo), así que un único
    /// request lento legítimo bastaba para sheddear a toda la flota.
    /// </summary>
    public int MinSamples { get; init; } = 200;

    /// <summary>
    /// Segundos que la sobrecarga debe sostenerse sin interrupción antes de activar el shedding. Un pico
    /// puntual no es sobrecarga; 0 activa en el primer refresco que la detecte.
    /// </summary>
    public int ActivationSeconds { get; init; } = 10;

    /// <summary>
    /// Histéresis de recuperación (0.0-1.0): ya activo, el shedding solo se apaga cuando p99 y tasa de
    /// 5xx bajan a esta fracción de sus umbrales. Sin banda, un p99 oscilando alrededor del umbral
    /// encendía y apagaba el shedding varias veces por minuto.
    /// </summary>
    public double RecoveryRatio { get; init; } = 0.8;

    /// <summary>
    /// Prefijos de ruta que ni se miden ni se sheddean: tráfico long-lived cuya duración no mide carga
    /// del servidor. Socket.IO entra por aquí completo, también su long-polling, que mantiene cada
    /// request abierto hasta que hay datos (y además que el chat cayera por shedding sería peor que la
    /// sobrecarga). Los upgrades WebSocket y /health quedan fuera siempre, estén o no en la lista.
    /// </summary>
    public string[] PassThroughPathPrefixes { get; init; } = ["/communication/socket.io"];

    /// <summary>
    /// Requests con un cuerpo mayor a esto no se miden (sí se sheddean): su duración la marca el
    /// ancho de banda de subida del cliente, no la carga del servidor.
    /// </summary>
    public long UnmeasuredRequestBodyBytes { get; init; } = 1_048_576;

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
