namespace TaxVision.Growth.Infrastructure.Persistence.Idempotency;

public enum ProcessedBusinessMessageStatus
{
    Processing = 1,
    Completed = 2,
    Failed = 3,
}
