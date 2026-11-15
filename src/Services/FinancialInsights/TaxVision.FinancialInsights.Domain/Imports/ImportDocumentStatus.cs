namespace TaxVision.FinancialInsights.Domain.Imports;

public enum ImportDocumentStatus
{
    AwaitingScan,
    Ready,
    Processing,
    Completed,
    NeedsReview,
    Rejected,
    Failed,
}
