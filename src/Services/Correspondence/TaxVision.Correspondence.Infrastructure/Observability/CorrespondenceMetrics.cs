using System.Diagnostics.Metrics;

namespace TaxVision.Correspondence.Infrastructure.Observability;

/// <summary>
/// Meter propio del servicio — registrado en OTel vía <c>AddTaxVisionOpenTelemetry</c> (el nombre
/// coincide con el <c>serviceName</c> pasado en Program.cs: "correspondence-service"). Mismo patrón
/// que <c>ConnectorsMetrics</c>/<c>PostmasterMetrics</c>: campos públicos <c>Counter</c>/
/// <c>Histogram</c>, el caller arma los tags inline (Fase 16, plan §29).
/// </summary>
public static class CorrespondenceMetrics
{
    private static readonly Meter Meter = new("correspondence-service");

    /// <summary>
    /// Plan §29 — mide el tramo completo Correspondence→Postmaster. Registrado adentro de
    /// <c>PostmasterClient.SendAsync</c> (Infrastructure), no en <c>SendDraftHandler</c>
    /// (Application) — <c>Application_should_not_depend_on_infrastructure</c>
    /// (CorrespondenceArchitectureTests) prohíbe justamente esa dirección; mismo criterio que
    /// <c>ConnectorsMetrics</c>, que solo se llama desde clientes de Infrastructure
    /// (GmailApiClient/GraphApiClient/...), nunca desde un handler de Application. Incluye lo que
    /// Postmaster tarda puertas adentro con Connectors/el proveedor real, porque para el usuario
    /// que apretó "Enviar" todo es una sola espera bloqueante. Tag "tenant".
    /// </summary>
    public static readonly Histogram<double> DraftSendDuration = Meter.CreateHistogram<double>(
        "correspondence_draft_send_duration_seconds"
    );

    /// <summary>
    /// Plan §29 — un envío por resultado, registrado en el mismo lugar que
    /// <see cref="DraftSendDuration"/>. Tags "tenant" y "status" (<c>sent</c>/<c>failed</c>/
    /// <c>suppressed</c> — <c>suppressed</c> es el único caso de falla que
    /// <c>PostmasterClient.SendAsync</c> puede distinguir de forma confiable, vía el
    /// <c>Error.Code</c> real que propaga <c>SendCorrespondenceMessageHandler.AllRecipientsSuppressed</c>
    /// de Postmaster tal cual — cualquier otro código de error cae en <c>failed</c>, nunca se
    /// inventa un tercer estado que el cliente no pueda producir de verdad).
    /// </summary>
    public static readonly Counter<long> DraftSendTotal = Meter.CreateCounter<long>("correspondence_draft_send_total");

    /// <summary>Plan §29/§30 — incrementado por <c>DraftCleanupJob</c> por cada Draft auto-descartado por abandono. Tag "tenant".</summary>
    public static readonly Counter<long> DraftAbandonedTotal = Meter.CreateCounter<long>(
        "correspondence_draft_abandoned_total"
    );
}
