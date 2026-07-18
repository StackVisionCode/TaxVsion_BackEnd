using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

public static class StripeCapabilities
{
    public static readonly ProviderCapabilities Instance = new()
    {
        Code = PaymentProviderCode.Stripe,
        DisplayName = "Stripe",
        BrandLogoAssetKey = "providers/stripe/logo.svg",
        SupportsOneShotCharge = true,
        SupportsRecurringCharge = true,
        SupportsHostedCheckoutRedirect = true,
        SupportsInlineElements = true,
        SupportsWebhookSignatureVerification = true,
        SupportedMethods = new HashSet<PaymentMethodKind>
        {
            PaymentMethodKind.Card,
            PaymentMethodKind.ApplePay,
            PaymentMethodKind.GooglePay,
            PaymentMethodKind.AchDebit,
            PaymentMethodKind.SepaDebit,
            PaymentMethodKind.Bnpl,
        },
        SupportsPartialRefund = true,
        Supports3DSecure = true,
        SupportsSavedPaymentMethods = true,
        SupportsMultiCurrency = true,
        SupportsMarketplaceConnect = true,
        SupportsIdempotencyKeys = true,
        SupportsCardTokenization = true,
        RequiresCustomerRegistrationBeforeCharge = false,
        SupportedCurrencies = new HashSet<string> { "USD", "EUR", "GBP", "CAD", "MXN" },
        SupportedCountries = new HashSet<string> { "US", "CA", "MX", "GB", "DE" },
        SupportedAmountRange = new MoneyRange(50, 99_999_900, "USD"),
        TypicalAuthorizeLatency = TimeSpan.FromMilliseconds(400),
        SuggestedRetryCount = 3,
    };
}
