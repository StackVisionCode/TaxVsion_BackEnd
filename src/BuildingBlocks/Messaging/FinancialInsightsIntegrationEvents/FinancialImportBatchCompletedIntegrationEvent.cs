using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.FinancialInsightsIntegrationEvents;

/// <summary>
/// Publicado por <c>TaxVision.FinancialInsights</c> una sola vez por lote, cuando todos sus documentos llegaron a un
/// estado final (procesado, en revisión, fallido o rechazado). Notification avisa a quien lo subió y, si lo subió el
/// cliente desde el portal, a la oficina. Los conteos permiten distinguir "listo" de "requiere revisión".
/// </summary>
[MessageIdentity("financial.import_batch.completed.v1")]
public sealed record FinancialImportBatchCompletedIntegrationEvent : IntegrationEvent
{
    public Guid BatchId { get; init; }
    public Guid CustomerId { get; init; }

    /// <summary>Usuario que creó el lote: staff de la oficina o el usuario del portal del cliente.</summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary><c>Staff</c> (CRM) o <c>Portal</c> (lo subió el cliente).</summary>
    public string Source { get; init; } = FinancialImportBatchSources.Staff;

    public int CompletedDocuments { get; init; }
    public int NeedsReviewDocuments { get; init; }
    public int FailedDocuments { get; init; }
    public int RejectedDocuments { get; init; }
    public DateTime CompletedAtUtc { get; init; }
}

public static class FinancialImportBatchSources
{
    public const string Staff = "Staff";
    public const string Portal = "Portal";
}
