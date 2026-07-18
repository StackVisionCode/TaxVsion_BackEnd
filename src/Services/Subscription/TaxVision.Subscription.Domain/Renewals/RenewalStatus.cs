namespace TaxVision.Subscription.Domain.Renewals;

public enum RenewalStatus
{
    Scheduled,
    Processing,
    Succeeded,
    Failed,
    Cancelled,
    Skipped,
    RetryScheduled,
}
