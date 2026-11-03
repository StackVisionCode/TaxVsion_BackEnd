using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;

namespace TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

/// <summary>
/// Saca los datos del pagador de un <see cref="PaymentIntent"/> creado con <c>latest_charge</c> y
/// <c>payment_method</c> expandidos. Manda el charge (lo que Stripe realmente cobró); el payment
/// method cubre lo que falte (p. ej. un 3DS pendiente aún sin charge). Sin datos → null.
/// </summary>
public static class StripePayerDetailsReader
{
    /// <summary>Lo que hay que expandir al crear/confirmar el PaymentIntent.</summary>
    public static IReadOnlyList<string> ExpandedFields { get; } = ["latest_charge", "payment_method"];

    public static ProviderPayerDetails? FromPaymentIntent(PaymentIntent? intent) =>
        intent is null ? null : Build(intent.LatestCharge, intent.PaymentMethod);

    /// <summary>Un rechazo llega como <see cref="StripeException"/>: el error trae el payment method.</summary>
    public static ProviderPayerDetails? FromError(StripeError? error) =>
        error is null ? null : Build(charge: null, error.PaymentMethod);

    private static ProviderPayerDetails? Build(Charge? charge, PaymentMethod? method)
    {
        var details = new ProviderPayerDetails(
            Name: FirstNonBlank(charge?.BillingDetails?.Name, method?.BillingDetails?.Name),
            Email: FirstNonBlank(charge?.BillingDetails?.Email, method?.BillingDetails?.Email),
            CardBrand: FirstNonBlank(charge?.PaymentMethodDetails?.Card?.Brand, method?.Card?.Brand),
            CardLast4: FirstNonBlank(charge?.PaymentMethodDetails?.Card?.Last4, method?.Card?.Last4)
        );

        return details is { Name: null, Email: null, CardBrand: null, CardLast4: null } ? null : details;
    }

    private static string? FirstNonBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first
        : !string.IsNullOrWhiteSpace(second) ? second
        : null;
}
