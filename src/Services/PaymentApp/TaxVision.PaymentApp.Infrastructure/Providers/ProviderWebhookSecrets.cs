using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentApp.Infrastructure.Providers;

/// <summary>Único punto que conoce dónde vive cada webhook secret. Intellipay no tiene uno
/// (no soporta HMAC — ver §44.9); Manual tampoco tiene webhooks.</summary>
public sealed class ProviderWebhookSecrets(IOptions<StripeOptions> stripeOptions) : IProviderWebhookSecrets
{
    public string? GetWebhookSecret(PaymentProviderCode code) => code switch
    {
        PaymentProviderCode.Stripe => stripeOptions.Value.WebhookSecret,
        _ => null,
    };
}
