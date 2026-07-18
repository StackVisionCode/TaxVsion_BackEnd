namespace TaxVision.PaymentClient.Domain.TenantPayments;

public enum PaymentStatus
{
    Pending = 1,
    Processing = 2,
    RequiresAction = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6,
    PartiallyRefunded = 7,
    Refunded = 8,
    ChargedBack = 9,
}
