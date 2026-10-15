using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

/// <summary>
/// Cobro off-session con un payment token previamente guardado en PayPal Vault. Usa el flow
/// <c>payment_source.token</c> de Orders API v2: crea la orden ya pre-aprobada por el token y
/// la captura idempotentemente. <c>PAYER_ACTION_REQUIRED</c> se mapea a
/// <see cref="OffSessionChargeStatus.RequiresUserAction"/> (equivalente a SCA de Stripe) — el
/// dunning entonces baja a flow manual porque este token ya no sirve off-session.
/// </summary>
public sealed class PayPalOffSessionCharger(PayPalGateway gateway, ILogger<PayPalOffSessionCharger> logger)
    : IOffSessionCharger
{
    public const string ProviderCodeValue = "PayPal";

    public string ProviderCode => ProviderCodeValue;

    public async Task<Result<OffSessionChargeOutcome>> ChargeAsync(
        OffSessionChargeRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.ProviderToken))
            return Result.Success(
                new OffSessionChargeOutcome(
                    OffSessionChargeStatus.Failed,
                    ProviderPaymentReference: null,
                    FailureCode: "PayPal.OffSession.MissingToken",
                    FailureMessage: "No PayPal payment token on file."
                )
            );

        var result = await gateway.ChargeWithPaymentTokenAsync(
            new PayPalChargeWithTokenRequest(
                AmountValue: PayPalGateway.FormatAmount(request.AmountCents),
                CurrencyCode: request.Currency,
                Description: "TAXVISION SAAS",
                PaymentTokenId: request.ProviderToken,
                IdempotencyKey: request.IdempotencyKey,
                CustomId: request.SaaSPaymentId.ToString("N")
            ),
            ct
        );

        if (result.IsFailure)
        {
            logger.LogWarning(
                "PayPal off-session charge for SaaSPayment {SaaSPaymentId} failed: {ErrorCode}: {ErrorMessage}",
                request.SaaSPaymentId,
                result.Error.Code,
                result.Error.Message
            );
            return Result.Success(
                new OffSessionChargeOutcome(
                    OffSessionChargeStatus.Failed,
                    ProviderPaymentReference: null,
                    FailureCode: result.Error.Code,
                    FailureMessage: result.Error.Message
                )
            );
        }

        var normalized = result.Value.Status.ToUpperInvariant();
        if (string.Equals(normalized, "PAYER_ACTION_REQUIRED", StringComparison.Ordinal))
            return Result.Success(
                new OffSessionChargeOutcome(
                    OffSessionChargeStatus.RequiresUserAction,
                    ProviderPaymentReference: result.Value.OrderId,
                    FailureCode: "PayPal.PayerActionRequired",
                    FailureMessage: "PayPal requires the payer to re-authorize the saved method."
                )
            );

        if (string.Equals(normalized, "COMPLETED", StringComparison.Ordinal))
            return Result.Success(
                new OffSessionChargeOutcome(
                    OffSessionChargeStatus.Succeeded,
                    ProviderPaymentReference: result.Value.CaptureId ?? result.Value.OrderId,
                    FailureCode: null,
                    FailureMessage: null
                )
            );

        return Result.Success(
            new OffSessionChargeOutcome(
                OffSessionChargeStatus.Failed,
                ProviderPaymentReference: result.Value.OrderId,
                FailureCode: $"PayPal.{normalized}",
                FailureMessage: result.Value.FailureReason ?? "PayPal declined the off-session charge."
            )
        );
    }
}
