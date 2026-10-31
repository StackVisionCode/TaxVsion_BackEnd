using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Common;

// Valores de tag de las métricas: conjuntos cerrados para que la cardinalidad no crezca con los datos.
public static class ClientFormsMetricValues
{
    public const string Returned = "returned";
    public const string Completed = "completed";

    public const string Ok = "ok";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Error = "error";

    public const string SourceEvent = "event";
    public const string SourceReconciliation = "reconciliation";
    public const string Unknown = "unknown";
    public const string Expired = "expired";

    public const string Granted = "granted";
    public const string Denied = "denied";

    public const string OtpIssued = "issued";
    public const string OtpVerified = "verified";
    public const string OtpFailed = "failed";
    public const string OtpLocked = "locked";

    public static string Of(FormFileVerdictStatus status) =>
        status switch
        {
            FormFileVerdictStatus.Available => "available",
            FormFileVerdictStatus.Infected => "infected",
            FormFileVerdictStatus.Blocked => "blocked",
            FormFileVerdictStatus.ScanFailed => "scan_failed",
            FormFileVerdictStatus.Deleted => "deleted",
            FormFileVerdictStatus.PendingReview => "pending_review",
            _ => Unknown,
        };
}
