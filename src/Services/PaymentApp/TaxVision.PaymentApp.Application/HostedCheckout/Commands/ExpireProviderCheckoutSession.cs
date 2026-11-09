using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.HostedCheckout.Commands;

/// <summary>Expira en el provider la sesión de un pago que se canceló localmente, para que el cliente no
/// pueda pagarla después. Lo publica <c>CheckoutCancelledOrExpiredSaaSPaymentConsumer</c> en la misma
/// transacción que el Cancelled; va a la cola local durable <c>payment-app-provider-calls</c>.</summary>
public sealed record ExpireProviderCheckoutSessionCommand(
    Guid SaaSPaymentId,
    PaymentProviderCode ProviderCode,
    string ProviderSessionId
);

public static class ExpireProviderCheckoutSessionHandler
{
    public static async Task Handle(
        ExpireProviderCheckoutSessionCommand command,
        IHostedCheckoutSessionCanceller canceller,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var result = await canceller.CancelAsync(command.ProviderCode, command.ProviderSessionId, ct);

        // Lanza para que Wolverine reintente y, si sigue fallando, lo deje en la DLQ.
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Could not expire {command.ProviderCode} checkout session for SaaSPayment {command.SaaSPaymentId}: {result.Error.Code}."
            );

        logger.LogInformation(
            "Expired {ProviderCode} checkout session for cancelled SaaSPayment {SaaSPaymentId} (already expired: {AlreadyExpired}).",
            command.ProviderCode,
            command.SaaSPaymentId,
            result.Value.AlreadyExpired
        );
    }
}
