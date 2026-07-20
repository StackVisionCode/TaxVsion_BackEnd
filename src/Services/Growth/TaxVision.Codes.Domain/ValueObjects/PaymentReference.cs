using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record PaymentReference
{
    public string Source { get; }
    public Guid PaymentId { get; }

    private PaymentReference(string source, Guid paymentId)
    {
        Source = source;
        PaymentId = paymentId;
    }

    public static Result<PaymentReference> Create(string source, Guid paymentId)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 100)
            return Result.Failure<PaymentReference>(
                new Error(
                    "Codes.PaymentReference.InvalidSource",
                    "Payment source is required and cannot exceed 100 characters."
                )
            );

        if (paymentId == Guid.Empty)
            return Result.Failure<PaymentReference>(
                new Error("Codes.PaymentReference.InvalidPaymentId", "PaymentId is required.")
            );

        return Result.Success(new PaymentReference(source.Trim(), paymentId));
    }
}
