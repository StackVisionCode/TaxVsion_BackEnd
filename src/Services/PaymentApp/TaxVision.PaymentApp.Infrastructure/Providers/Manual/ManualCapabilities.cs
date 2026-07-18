using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Manual;

public static class ManualCapabilities
{
    public static readonly ProviderCapabilities Instance = new()
    {
        Code = PaymentProviderCode.Manual,
        DisplayName = "Manual (ACH / Check / Wire)",
        BrandLogoAssetKey = "providers/manual/logo.svg",
        SupportsOneShotCharge = true,
        SupportsRecurringCharge = false,
        SupportsHostedCheckoutRedirect = false,
        SupportsInlineElements = false,
        SupportsWebhookSignatureVerification = false,
        SupportedMethods = new HashSet<PaymentMethodKind> { PaymentMethodKind.Manual, PaymentMethodKind.Bank, PaymentMethodKind.Cash },
        SupportsPartialRefund = true,
        Supports3DSecure = false,
        SupportsSavedPaymentMethods = false,
        SupportsMultiCurrency = true,
        SupportsMarketplaceConnect = false,
        SupportsIdempotencyKeys = true,
        SupportsCardTokenization = false,
        RequiresCustomerRegistrationBeforeCharge = false,
        SupportedCurrencies = new HashSet<string> { "USD" },
        SupportedCountries = new HashSet<string> { "US" },
        SupportedAmountRange = null,
        TypicalAuthorizeLatency = TimeSpan.Zero,
        SuggestedRetryCount = 0,
    };
}
