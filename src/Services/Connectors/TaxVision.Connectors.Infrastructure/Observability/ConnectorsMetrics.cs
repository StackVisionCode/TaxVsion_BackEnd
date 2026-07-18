using System.Diagnostics.Metrics;

namespace TaxVision.Connectors.Infrastructure.Observability;

/// <summary>
/// Meter propio del servicio — registrado en OTel vía <c>AddTaxVisionOpenTelemetry</c> (el nombre
/// coincide con el <c>serviceName</c> pasado en Program.cs: "connectors-service").
/// </summary>
public static class ConnectorsMetrics
{
    private static readonly Meter Meter = new("connectors-service");

    public static readonly Counter<long> RateLimitHits = Meter.CreateCounter<long>("connectors_rate_limit_hits_total");

    public static readonly Counter<long> CircuitBreakerOpened = Meter.CreateCounter<long>(
        "connectors_circuit_breaker_opened_total"
    );

    public static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>("connectors_retry_attempts_total");

    /// <summary>D3 §3.6 — tag "provider" (Gmail/Graph).</summary>
    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>("connectors_messages_sent_total");

    /// <summary>D3 §3.6 — tags "provider" y "reason" (<see cref="TaxVision.Connectors.Application.Providers.SendFailureReason"/>).</summary>
    public static readonly Counter<long> SendFailures = Meter.CreateCounter<long>("connectors_send_failures_total");

    /// <summary>ReconciliationJob (README §37.8) — cuentas Active procesadas por corrida, tag "provider".</summary>
    public static readonly Counter<long> ReconciliationAccountsScanned = Meter.CreateCounter<long>(
        "connectors_reconciliation_accounts_scanned_total"
    );

    /// <summary>Mensajes publicados durante un pase de reconciliación, tag "provider". Incluye tanto catch-up inicial (IMAP, cuentas recién activadas) como mensajes genuinamente recuperados — ver ReconciliationMessagesRecovered para el subconjunto que sí es señal de degradación del push.</summary>
    public static readonly Counter<long> ReconciliationMessagesFound = Meter.CreateCounter<long>(
        "connectors_reconciliation_messages_found_total"
    );

    /// <summary>Subconjunto de ReconciliationMessagesFound que SÍ es señal de que el push (Gmail Pub/Sub o Graph notification) no entregó algo — cursor ya existía (no es catch-up inicial) y el proveedor es push-backed (Gmail/Graph, nunca IMAP). En operación normal debería quedarse en 0; una tendencia creciente indica degradación del path de push. Tag "provider".</summary>
    public static readonly Counter<long> ReconciliationMessagesRecovered = Meter.CreateCounter<long>(
        "connectors_reconciliation_messages_recovered_total"
    );

    /// <summary>Cuentas cuyo pase de reconciliación falló (excepción o Result.IsFailure), tag "provider".</summary>
    public static readonly Counter<long> ReconciliationErrors = Meter.CreateCounter<long>(
        "connectors_reconciliation_errors_total"
    );
}
