using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Domain.ValueObjects;

/// <summary>
/// Token opaco que identifica al "customer" en el provider (Stripe <c>cus_xxx</c>, Intellipay
/// <c>custId</c>). Estructuralmente igual a <see cref="ExternalPaymentReference"/> pero
/// deliberadamente un tipo distinto — mezclar "id de customer" con "id de charge" en el
/// código de un sistema de pagos es la clase de bug que este VO existe para prevenir en
/// tiempo de compilación.
/// </summary>
public sealed record ProviderCustomerReference
{
    public PaymentProviderCode Provider { get; }
    public string Value { get; }

    private ProviderCustomerReference(PaymentProviderCode provider, string value)
    {
        Provider = provider;
        Value = value;
    }

    public static Result<ProviderCustomerReference> Create(PaymentProviderCode provider, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<ProviderCustomerReference>(
                new Error("ProviderCustomerReference.Empty", "ProviderCustomerReference value is required.")
            );

        if (value.Length > 200)
            return Result.Failure<ProviderCustomerReference>(
                new Error(
                    "ProviderCustomerReference.TooLong",
                    "ProviderCustomerReference value must be 200 characters or fewer."
                )
            );

        return Result.Success(new ProviderCustomerReference(provider, value.Trim()));
    }

    public override string ToString() => $"{Provider}:{Value}";
}
