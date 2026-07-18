using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.ValueObjects;

public sealed record ExternalPaymentReference
{
    public PaymentProviderCode Provider { get; }
    public string Value { get; }

    private ExternalPaymentReference(PaymentProviderCode provider, string value)
    {
        Provider = provider;
        Value = value;
    }

    public static Result<ExternalPaymentReference> Create(PaymentProviderCode provider, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<ExternalPaymentReference>(
                new Error("ExternalPaymentReference.Empty", "ExternalPaymentReference value is required.")
            );

        if (value.Length > 200)
            return Result.Failure<ExternalPaymentReference>(
                new Error(
                    "ExternalPaymentReference.TooLong",
                    "ExternalPaymentReference value must be 200 characters or fewer."
                )
            );

        return Result.Success(new ExternalPaymentReference(provider, value.Trim()));
    }

    public override string ToString() => $"{Provider}:{Value}";
}
