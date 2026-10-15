using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentApp.Infrastructure.Providers;

/// <summary>Resuelve el canceler correcto por <see cref="PaymentProviderCode"/>. Separado de
/// <c>IPaymentAdapterFactory</c> porque no todos los providers implementan cancel (ver interfaz).</summary>
public sealed class DefaultHostedCheckoutSessionCanceller(
    StripeHostedCheckoutCanceller stripe,
    PayPalHostedCheckoutCanceller paypal,
    ILogger<DefaultHostedCheckoutSessionCanceller> logger
) : IHostedCheckoutSessionCanceller
{
    public Task<Result<CancelHostedCheckoutResult>> CancelAsync(
        PaymentProviderCode providerCode,
        string providerSessionId,
        CancellationToken ct = default
    ) =>
        providerCode switch
        {
            PaymentProviderCode.Stripe => stripe.CancelAsync(providerSessionId, ct),
            PaymentProviderCode.PayPal => paypal.CancelAsync(providerSessionId, ct),
            _ => NotSupported(providerCode, providerSessionId),
        };

    private Task<Result<CancelHostedCheckoutResult>> NotSupported(PaymentProviderCode code, string sessionId)
    {
        logger.LogInformation(
            "Hosted checkout cancel is not implemented for provider {ProviderCode} (session {SessionId}); no-op.",
            code,
            sessionId
        );
        return Task.FromResult(Result.Success(new CancelHostedCheckoutResult(AlreadyExpired: false)));
    }
}
