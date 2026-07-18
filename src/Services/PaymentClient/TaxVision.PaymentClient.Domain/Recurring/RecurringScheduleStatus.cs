namespace TaxVision.PaymentClient.Domain.Recurring;

/// <summary>
/// Pending ──job de ejecución la toma──▶ Processing
/// Processing ──cobro exitoso──▶ Executed
/// Processing ──cobro falla, quedan reintentos──▶ RetryPending
/// Processing ──cobro falla, reintentos agotados──▶ Failed
/// RetryPending ──job de retry la toma──▶ Processing
/// Pending/RetryPending ──admin skip──▶ Skipped
/// </summary>
public enum RecurringScheduleStatus
{
    Pending = 1,
    Processing = 2,
    Executed = 3,
    Failed = 4,
    Skipped = 5,
    RetryPending = 6,
}
