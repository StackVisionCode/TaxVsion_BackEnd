namespace TaxVision.Customer.Domain.Imports;

public enum ImportStatus
{
    Queued,
    Validating,
    Applying,
    Completed,
    Failed,
    Canceling,
    Canceled,
}
