using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions.Payments;

/// <summary>
/// Matriz declarativa de lo que un provider soporta. El orquestador la consulta *antes* de
/// despachar un intent al adapter — nunca se descubre por prueba y error contra el provider
/// real. Cada <see cref="TaxVision.PaymentApp.Domain.ValueObjects.PaymentProviderCode"/>
/// registrado expone exactamente una instancia de esto (guardrail §44.2/§44.1 ley 4).
/// </summary>
public sealed record ProviderCapabilities
{
    public required PaymentProviderCode Code { get; init; }
    public required string DisplayName { get; init; }
    public required string BrandLogoAssetKey { get; init; }

    public required bool SupportsOneShotCharge { get; init; }
    public required bool SupportsRecurringCharge { get; init; }
    public required bool SupportsHostedCheckoutRedirect { get; init; }
    public required bool SupportsInlineElements { get; init; }
    public required bool SupportsWebhookSignatureVerification { get; init; }

    public required IReadOnlySet<PaymentMethodKind> SupportedMethods { get; init; }

    public required bool SupportsPartialRefund { get; init; }
    public required bool Supports3DSecure { get; init; }
    public required bool SupportsSavedPaymentMethods { get; init; }
    public required bool SupportsMultiCurrency { get; init; }
    public required bool SupportsMarketplaceConnect { get; init; }
    public required bool SupportsIdempotencyKeys { get; init; }
    public required bool SupportsCardTokenization { get; init; }
    public required bool RequiresCustomerRegistrationBeforeCharge { get; init; }

    public required IReadOnlySet<string> SupportedCurrencies { get; init; }
    public required IReadOnlySet<string> SupportedCountries { get; init; }
    public MoneyRange? SupportedAmountRange { get; init; }

    public required TimeSpan TypicalAuthorizeLatency { get; init; }
    public required int SuggestedRetryCount { get; init; }
}
