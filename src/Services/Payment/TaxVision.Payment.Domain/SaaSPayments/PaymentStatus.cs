namespace TaxVision.Payment.Domain.SaaSPayments;

public enum PaymentStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Refunded
}
