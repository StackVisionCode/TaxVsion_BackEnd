using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>Aplica a un pago lo que respondió un cobro off-session.</summary>
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
        var nowUtc = DateTime.UtcNow;

        // El provider devuelve una referencia útil solo cuando pudo crear el intent — por eso no se
        // exige para los Failed sin referencia (red caída, auth fallida antes de hablar con el banco).
        if (!string.IsNullOrWhiteSpace(outcome.ProviderPaymentReference))
        {
            var referenceResult = ExternalPaymentReference.Create(provider, outcome.ProviderPaymentReference);
            if (referenceResult.IsSuccess)
            {
                payment.MarkProcessing(
                    referenceResult.Value,
                    outcome.Status.ToString(),
                    providerResponseBody: null,
                    actorUserId: Guid.Empty,
                    nowUtc
                );
            }
        }

        switch (outcome.Status)
        {
            case OffSessionChargeStatus.Succeeded:
                if (payment.Status == PaymentStatus.Pending)
                {
                    // Sin referencia no se puede marcar Processing — pasar directo a Succeeded igual
                    // publicaría el succeeded (que incluye ExternalPaymentReference en el evento),
                    // así que trato esto como Failed sin registro: el flow manual retomará.
                    SaaSPaymentChargeOutcome.FailPayment(
                        payment,
                        new Error("OffSession.MissingReference", "Provider returned Succeeded with no reference."),
                        Guid.Empty,
                        SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc),
                        metrics
                    );
                    return;
                }
                payment.MarkSucceeded(nowUtc, Guid.Empty);
                metrics.RecordSucceeded(provider.ToString(), payment.Type.ToString());
                break;
            case OffSessionChargeStatus.RequiresUserAction:
                // SCA/3DS: marcar RequiresAction y DEJAR que el publisher caiga al legacy — el
                // RequiresAction no es un fallo pero tampoco cierra el intent; el banner "Pay now"
                // de la landing arranca el on-session cuando el user vuelve.
                if (payment.Status == PaymentStatus.Pending)
                {
                    SaaSPaymentChargeOutcome.FailPayment(
                        payment,
                        new Error(
                            outcome.FailureCode ?? "authentication_required",
                            outcome.FailureMessage ?? "Issuing bank requires customer authentication."
                        ),
                        Guid.Empty,
                        SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc),
                        metrics
                    );
                }
                else
                {
                    payment.MarkRequiresAction(
                        outcome.FailureCode ?? "authentication_required",
                        nextActionUrl: string.Empty,
                        actorUserId: Guid.Empty,
                        nowUtc
                    );
                }
                logger.LogInformation(
                    "Off-session renewal requires user action (3DS/SCA) for payment {PaymentId}; fallback to Pay now banner.",
                    payment.Id
                );
                break;
            case OffSessionChargeStatus.Processing:
            case OffSessionChargeStatus.OutcomeUnknown:
                // D-PAY-13: el cargo pudo salir. Con referencia lo consulta el job de Processing; sin ella sigue
                // Pending con el intento grabado y lo busca la reconciliación. Sin dunning ni clave nueva.
                logger.LogWarning(
                    "Off-session charge of payment {PaymentId} has no final outcome ({Status}); left for reconciliation.",
                    payment.Id,
                    outcome.Status
                );
                break;
            case OffSessionChargeStatus.Failed:
            default:
                SaaSPaymentChargeOutcome.FailPayment(
                    payment,
                    new Error(
                        outcome.FailureCode ?? "Provider.Failed",
                        outcome.FailureMessage ?? "Off-session charge was declined."
                    ),
                    Guid.Empty,
                    // Con referencia, el intento ya quedó registrado en MarkProcessing: no contarlo dos veces.
                    SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(
                        payment,
                        nowUtc,
                        failedAttemptRecorded: payment.Status == PaymentStatus.Processing
                    ),
                    metrics
                );
                break;
        }
    }
}
