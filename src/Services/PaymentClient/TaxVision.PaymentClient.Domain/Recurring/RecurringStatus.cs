namespace TaxVision.PaymentClient.Domain.Recurring;

/// <summary>
/// Active ──pause──▶ Paused
/// Paused ──resume──▶ Active
/// Active/Paused ──suspend (auto o admin)──▶ Suspended
/// Suspended ──admin reactivate──▶ Active
/// Active ──todos los schedules ejecutados──▶ Completed
/// Cualquier estado no terminal ──cancel──▶ Cancelled
/// </summary>
public enum RecurringStatus
{
    Active = 1,
    Paused = 2,
    Suspended = 3,
    Completed = 4,
    Cancelled = 5,
}
