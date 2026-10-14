using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.HostedCheckout.Commands;

/// <summary>Cancel/expire best-effort de una Checkout Session hosteada. Idempotente: una sesión ya
/// expirada/completada devuelve <c>AlreadyExpired=true</c>. Lo invoca Subscription cuando el usuario cierra la
/// pasarela sin pagar; el guard local ya movió la intención a Cancelled, así que un fallo acá no bloquea.</summary>
public sealed record CancelHostedCheckoutSessionCommand(string ProviderSessionId, PaymentProviderCode? ProviderCode);

public sealed record CancelHostedCheckoutSessionResponse(bool AlreadyExpired);

public static class CancelHostedCheckoutSessionHandler
{
    public static async Task<Result<CancelHostedCheckoutSessionResponse>> Handle(
        CancelHostedCheckoutSessionCommand command,
        ISaaSPaymentRepository payments,
        IHostedCheckoutSessionCanceller canceller,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.ProviderSessionId))
            return Result.Failure<CancelHostedCheckoutSessionResponse>(
                new Error("SaaSPayment.InvalidCheckoutSession", "ProviderSessionId is required.")
            );

        var providerCode = command.ProviderCode;
        if (providerCode is null)
        {
            var payment = await payments.GetByProviderCheckoutSessionIdAsync(command.ProviderSessionId, ct);
            if (payment is null)
                return Result.Failure<CancelHostedCheckoutSessionResponse>(
                    new Error("SaaSPayment.NotFound", "No payment matches that checkout session.")
                );

            providerCode = payment.ProviderCode;

            // Si el pago local ya está en estado terminal, no es necesario llamar al provider: el guard local ya
            // ganó y la sesión expiró o se completó. Devolvemos alreadyExpired=true para que el caller lo trate
            // como idempotente.
            if (
                payment.Status
                is PaymentStatus.Succeeded
                    or PaymentStatus.Cancelled
                    or PaymentStatus.Refunded
                    or PaymentStatus.PartiallyRefunded
                    or PaymentStatus.ChargedBack
            )
            {
                logger.LogInformation(
                    "Hosted checkout cancel skipped for session {SessionId}: payment already {Status}.",
                    command.ProviderSessionId,
                    payment.Status
                );
                return Result.Success(new CancelHostedCheckoutSessionResponse(AlreadyExpired: true));
            }
        }

        var result = await canceller.CancelAsync(providerCode.Value, command.ProviderSessionId, ct);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Hosted checkout cancel failed for session {SessionId} on {ProviderCode}: {ErrorCode}",
                command.ProviderSessionId,
                providerCode.Value,
                result.Error.Code
            );
            return Result.Failure<CancelHostedCheckoutSessionResponse>(result.Error);
        }

        return Result.Success(new CancelHostedCheckoutSessionResponse(result.Value.AlreadyExpired));
    }
}
