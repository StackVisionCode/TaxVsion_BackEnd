using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions.Payments;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

/// <summary>PayPal no expone un endpoint de "cancel order" para órdenes creadas y no aprobadas: expiran solas
/// (3h por default). Devolvemos Success con AlreadyExpired=false y lo registramos — un VOID sobre authorizations
/// aprobadas-pero-no-capturadas es trabajo separado y no se trata acá.</summary>
public sealed class PayPalHostedCheckoutCanceller(ILogger<PayPalHostedCheckoutCanceller> logger)
{
    public Task<Result<CancelHostedCheckoutResult>> CancelAsync(string sessionId, CancellationToken ct)
    {
        logger.LogInformation(
            "PayPal has no explicit cancel endpoint for order {OrderId}; it will expire naturally.",
            sessionId
        );
        return Task.FromResult(Result.Success(new CancelHostedCheckoutResult(AlreadyExpired: false)));
    }
}
