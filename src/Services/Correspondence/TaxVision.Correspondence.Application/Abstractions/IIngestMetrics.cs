namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>Métricas de la ingesta. La implementación vive en Infrastructure (meter del servicio).</summary>
public interface IIngestMetrics
{
    /// <summary>Un correo de cliente que el dominio rechazó aun normalizado (<c>correspondence_ingest_rejected_total</c>).</summary>
    void IngestRejected(string code);
}
