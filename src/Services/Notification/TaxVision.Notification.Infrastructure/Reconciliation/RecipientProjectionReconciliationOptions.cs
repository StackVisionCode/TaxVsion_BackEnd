namespace TaxVision.Notification.Infrastructure.Reconciliation;

/// <summary>Backfill de las proyecciones de audiencia (owner del tenant, cuenta de portal) desde Auth.</summary>
public sealed class RecipientProjectionReconciliationOptions
{
    public const string SectionName = "Notification:RecipientProjections";

    /// <summary>Local: http://localhost:5124. En Docker: http://auth-api:8080.</summary>
    public string AuthBaseUrl { get; set; } = "http://localhost:5124";

    public bool ReconciliationEnabled { get; set; } = true;

    public int ReconciliationIntervalHours { get; set; } = 12;

    public int ReconciliationPageSize { get; set; } = 200;
}
