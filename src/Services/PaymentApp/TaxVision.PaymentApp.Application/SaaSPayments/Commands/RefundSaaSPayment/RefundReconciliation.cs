using BuildingBlocks.Common;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>Un reembolso que el proveedor hizo (o pudo hacer) sin registro local: la intención queda cerrada para no
/// reenviarse, el pago entra a la bandeja de revisión y queda auditado. La resincronización del proveedor
/// (<c>charge.refunded</c>) o el admin cierran la diferencia.</summary>
internal static class RefundReconciliation
{
    /// <summary>Devuelve true si el pago quedó marcado ahora; false si ya tenía otra marca de revisión.</summary>
    public static async Task<bool> MarkAsync(
        SaaSPayment payment,
        Guid requestId,
        string? providerRefundReference,
        string failureCode,
        IPaymentAuditLogWriter audit,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var request = payment.RefundRequests.First(candidate => candidate.Id == requestId);
        var marked = payment.MarkRefundNeedsReconciliation(requestId, providerRefundReference, failureCode, nowUtc);
        var flagged = marked.IsSuccess && marked.Value;

        metrics.RecordPaymentReviewRequired(
            payment.ProviderCode.ToString(),
            SaaSPayment.ReviewReasonRefundNeedsReconciliation
        );
        logger.LogError(
            "SaaSPayment {SaaSPaymentId} refund request {RefundRequestId} needs reconciliation ({FailureCode}): provider refund {ProviderRefundReference} of {AmountCents} {Currency} is not recorded. It will not be sent again. Flagged for review: {Flagged}.",
            payment.Id,
            requestId,
            failureCode,
            providerRefundReference,
            request.Amount.AmountCents,
            request.Amount.Currency,
            flagged
        );

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            PaymentAuditAction.SaaSPaymentReviewRequired,
            request.RequestedByUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                ReviewReasonCode = SaaSPayment.ReviewReasonRefundNeedsReconciliation,
                RefundRequestId = requestId,
                ProviderRefundReference = providerRefundReference,
                RefundAmountCents = request.Amount.AmountCents,
                FailureCode = failureCode,
                FlaggedForReview = flagged,
            },
            reason: request.Reason,
            nowUtc,
            ct
        );
        return flagged;
    }
}
