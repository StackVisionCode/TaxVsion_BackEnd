using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>
/// Un éxito del proveedor que no se puede aplicar (pago ya cerrado sin cobro, o monto distinto) es dinero
/// cobrado sin registro: se marca el pago para revisión manual, con métrica y log de error. Nunca reembolsa
/// ni aplica beneficios; eso lo decide el admin. Lo comparten el webhook y el reconcile del hosted checkout.
/// </summary>
public static class LateProviderSuccessPolicy
{
    /// <summary>Estados sin cobro sobre los que un éxito tardío del proveedor requiere revisión.</summary>
    public static bool IsClosedWithoutCharge(PaymentStatus status) =>
        status is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Pending;

    /// <summary>Marca el pago. Devuelve true solo la primera vez (la métrica y el log no se repiten).</summary>
    public static bool Flag(
        SaaSPayment payment,
        string reasonCode,
        string? providerReference,
        long? paidAmountCents,
        string? paidCurrency,
        IPaymentAppMetrics? metrics,
        ILogger logger,
        DateTime nowUtc
    )
    {
        if (payment.ReviewRequiredAtUtc is not null)
            return false;

        var flagged = payment.FlagForReview(reasonCode, providerReference, paidAmountCents, paidCurrency, nowUtc);
        if (flagged.IsFailure)
            return false;

        metrics?.RecordPaymentReviewRequired(payment.ProviderCode.ToString(), reasonCode);
        logger.LogError(
            "SaaSPayment {SaaSPaymentId} flagged for review ({ReasonCode}): provider {Provider} reported a successful charge {ProviderReference} ({PaidAmountCents} {PaidCurrency}) while the payment is {Status} expecting {AmountCents} {Currency}.",
            payment.Id,
            reasonCode,
            payment.ProviderCode,
            providerReference,
            paidAmountCents,
            paidCurrency,
            payment.Status,
            payment.Amount.AmountCents,
            payment.Amount.Currency
        );
        return true;
    }

    /// <summary>Motivo de revisión para un éxito del proveedor que no se pudo aplicar; null si no aplica
    /// (p. ej. un éxito repetido sobre un pago ya Succeeded sigue como Stale).</summary>
    public static string? ReviewReasonFor(SaaSPayment payment, PaymentStatus providerStatus, Error transitionError)
    {
        if (providerStatus != PaymentStatus.Succeeded)
            return null;

        if (transitionError.Code == "WebhookEvent.AmountMismatch")
            return SaaSPayment.ReviewReasonAmountMismatch;

        return transitionError.Code == "SaaSPayment.InvalidTransition" && IsClosedWithoutCharge(payment.Status)
            ? SaaSPayment.ReviewReasonLateProviderSuccess
            : null;
    }
}
