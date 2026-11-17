using System.Globalization;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;

/// <summary>Precio de lista, descuento y códigos (enmascarados) del alta, ya formateados para el correo.</summary>
public static class OnboardingEmailPricing
{
    public sealed record EmailDiscount(string? GrossFormatted, string? DiscountFormatted, string? DiscountSummary);

    public static EmailDiscount Discount(TenantOnboarding onboarding)
    {
        if (onboarding.ReceiptGrossAmountCents() is not { } gross)
            return new EmailDiscount(null, null, null);

        var currency = onboarding.Currency ?? "USD";
        var discounts = onboarding.ReceiptDiscounts();
        return new EmailDiscount(
            Format(gross, currency),
            Format(discounts.Sum(discount => discount.AmountCents), currency),
            string.Join(" + ", discounts.Select(discount => discount.Label))
        );
    }

    /// <summary>El aviso de registro con el descuento del alta, si lo hubo.</summary>
    public static OnboardingRegistrationReadyIntegrationEvent WithDiscountOf(
        this OnboardingRegistrationReadyIntegrationEvent evt,
        TenantOnboarding onboarding
    )
    {
        var discount = Discount(onboarding);
        return evt with
        {
            GrossFormatted = discount.GrossFormatted,
            DiscountFormatted = discount.DiscountFormatted,
            DiscountSummary = discount.DiscountSummary,
        };
    }

    private static string Format(long amountCents, string currency) =>
        $"{(amountCents / 100m).ToString("F2", CultureInfo.InvariantCulture)} {currency}";
}
