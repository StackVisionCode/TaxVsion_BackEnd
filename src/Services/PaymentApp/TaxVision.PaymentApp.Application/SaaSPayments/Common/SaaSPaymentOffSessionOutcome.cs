using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>Traduce lo que respondió un cobro off-session y registra métricas y logs. Las transiciones las decide
/// <see cref="SaaSPayment.ApplyOffSessionOutcome"/>.</summary>
public static class SaaSPaymentOffSessionOutcome
{
    public static void Apply(
        SaaSPayment payment,
        OffSessionChargeOutcome outcome,
        PaymentProviderCode provider,
        IPaymentAppMetrics metrics,
        ILogger logger
    )
    {
        var applied = payment.ApplyOffSessionOutcome(ToResponse(outcome, provider), DateTime.UtcNow);

        if (applied.ProviderConfirmed)
            metrics.RecordSucceeded(provider.ToString(), payment.Type.ToString());

        if (applied.FailureCode is { } failureCode)
            metrics.RecordFailed(payment.ProviderCode.ToString(), payment.Type.ToString(), failureCode);

        if (applied.NeedsUserAction)
            logger.LogInformation(
                "Off-session renewal requires user action (3DS/SCA) for payment {PaymentId}; fallback to Pay now banner.",
                payment.Id
            );

        if (applied.NoFinalOutcome)
            logger.LogWarning(
                "Off-session charge of payment {PaymentId} has no final outcome ({Status}); left for reconciliation.",
                payment.Id,
                outcome.Status
            );
    }

    private static OffSessionChargeResponse ToResponse(OffSessionChargeOutcome outcome, PaymentProviderCode provider)
    {
        var reference = string.IsNullOrWhiteSpace(outcome.ProviderPaymentReference)
            ? null
            : ExternalPaymentReference.Create(provider, outcome.ProviderPaymentReference);

        return new OffSessionChargeResponse(
            outcome.Status switch
            {
                OffSessionChargeStatus.Succeeded => OffSessionChargeResult.Succeeded,
                OffSessionChargeStatus.RequiresUserAction => OffSessionChargeResult.RequiresUserAction,
                OffSessionChargeStatus.Processing or OffSessionChargeStatus.OutcomeUnknown =>
                    OffSessionChargeResult.NoFinalOutcome,
                _ => OffSessionChargeResult.Declined,
            },
            reference is { IsSuccess: true } ? reference.Value : null,
            outcome.Status.ToString(),
            outcome.FailureCode,
            outcome.FailureMessage
        );
    }
}
