using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

public static class PayPalCapabilities
{
    public static readonly ProviderCapabilities Instance = new()
    {
        Code = PaymentProviderCode.PayPal,
        DisplayName = "PayPal",
        SupportsOneShotCharge = true,
        SupportsRecurringCharge = false,
        SupportsHostedCheckoutRedirect = true,
        SupportsInlineElements = false,
        SupportsWebhookSignatureVerification = true,
        SupportedMethods = new HashSet<PaymentMethodKind> { PaymentMethodKind.Wallet },
        SupportsPartialRefund = true,
        Supports3DSecure = false,
        SupportsSavedPaymentMethods = false,
        SupportsMultiCurrency = true,
        SupportsMarketplaceConnect = false,
        SupportsIdempotencyKeys = true,
        SupportsCardTokenization = false,
        RequiresCustomerRegistrationBeforeCharge = false,
        SupportedCurrencies = new HashSet<string> { "USD", "EUR", "GBP", "CAD", "MXN" },
        SupportedCountries = new HashSet<string> { "US", "CA", "MX", "GB", "DE" },
        SupportedAmountRange = new MoneyRange(100, 99_999_900, "USD"),
        TypicalAuthorizeLatency = TimeSpan.FromMilliseconds(900),
        SuggestedRetryCount = 3,
    };
}
