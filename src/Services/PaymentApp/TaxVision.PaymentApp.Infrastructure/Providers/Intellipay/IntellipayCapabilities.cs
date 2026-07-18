using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Intellipay;

public static class IntellipayCapabilities
{
    public static readonly ProviderCapabilities Instance = new()
    {
        Code = PaymentProviderCode.Intellipay,
        DisplayName = "Intellipay",
        BrandLogoAssetKey = "providers/intellipay/logo.svg",
        SupportsOneShotCharge = true,
        SupportsRecurringCharge = true,
        SupportsHostedCheckoutRedirect = true,
        SupportsInlineElements = false,
        SupportsWebhookSignatureVerification = false,
        SupportedMethods = new HashSet<PaymentMethodKind> { PaymentMethodKind.Card, PaymentMethodKind.AchDebit },
        SupportsPartialRefund = true,
        Supports3DSecure = false,
        SupportsSavedPaymentMethods = true,
        SupportsMultiCurrency = false,
        SupportsMarketplaceConnect = false,
        SupportsIdempotencyKeys = false,
        SupportsCardTokenization = true,
        RequiresCustomerRegistrationBeforeCharge = true,
        SupportedCurrencies = new HashSet<string> { "USD" },
        SupportedCountries = new HashSet<string> { "US" },
        SupportedAmountRange = new MoneyRange(100, 999_999_00, "USD"),
        TypicalAuthorizeLatency = TimeSpan.FromMilliseconds(1200),
        SuggestedRetryCount = 2,
    };
}
