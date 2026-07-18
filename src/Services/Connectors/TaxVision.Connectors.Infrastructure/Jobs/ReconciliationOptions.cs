namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>
/// Cadence del pase compartido de reconciliación sobre TODAS las cuentas Active (Gmail, Graph,
/// IMAP) — ver ReconciliationJob y README §37.8 para el detalle completo. Para Gmail/Graph esto es
/// un safety net detrás del push (Fase 7): recupera pushes de Pub/Sub perdidos, notificaciones de
/// Graph que nunca llegaron, o un watch subscription que venció silenciosamente entre corridas de
/// WatchRenewalJob. Para IMAP, que no tiene NINGÚN mecanismo de push (no existe un estándar
/// genérico — ver SetupWatchHandler), este intervalo ES el mecanismo de sync: es la única forma en
/// que mail nueva se detecta post-conexión.
///
/// Decisión de diseño: un único intervalo compartido para los 3 proveedores, en vez de una cadencia
/// separada más ajustada para IMAP. Se evaluó explícitamente la alternativa (IMAP más frecuente,
/// Gmail/Graph más espaciado) — se descartó por simplicidad: un segundo BackgroundService con su
/// propio timer para separar cadencias es complejidad real (dos loops, dos StartupDelay, dos
/// puntos de fallo) a cambio de un beneficio marginal (las llamadas a Gmail/Graph vía
/// GetHistoryAsync son baratas y ya están protegidas por IProviderRateLimiter/circuit breaker — no
/// hay ahorro de cuota significativo en espaciarlas más). El default (15 min) prioriza que IMAP,
/// que depende 100% de este job, tenga una libertad razonable en vez de optimizar para minimizar
/// llamadas a Gmail/Graph que de por sí son casi siempre no-ops (el push ya sincronizó).
/// </summary>
public sealed class ReconciliationOptions
{
    public const string SectionName = "Connectors:Reconciliation";

    public int IntervalMinutes { get; set; } = 15;
}
