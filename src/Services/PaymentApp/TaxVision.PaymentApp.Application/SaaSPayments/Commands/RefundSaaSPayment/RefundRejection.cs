using BuildingBlocks.Common;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>El proveedor rechazó el reembolso: la intención se cierra sin dinero movido, queda auditada y, si se pide,
/// el pago entra a la bandeja de revisión (<c>RefundRejected</c>).</summary>
internal static class RefundRejection
{
    public static async Task MarkAsync(
        SaaSPayment payment,
        RefundRequest request,
        string rejectionCode,
        bool flagForReview,
        IPaymentAuditLogWriter audit,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        payment.RejectRefundRequest(request.Id, rejectionCode, nowUtc);

        var flagged = false;
        if (flagForReview)
        {
            var flag = payment.FlagRefundForReview(
                SaaSPayment.ReviewReasonRefundRejected,
                providerReference: null,
                request.Amount,
                nowUtc
            );
            flagged = flag.IsSuccess && flag.Value;
            metrics.RecordPaymentReviewRequired(
                payment.ProviderCode.ToString(),
                SaaSPayment.ReviewReasonRefundRejected
            );
        }

        logger.LogError(
            "SaaSPayment {SaaSPaymentId} refund request {RefundRequestId} of {AmountCents} {Currency} was rejected by {Provider}: {RejectionCode}. Flagged for review: {Flagged}.",
            payment.Id,
            request.Id,
            request.Amount.AmountCents,
            request.Amount.Currency,
            payment.ProviderCode,
            rejectionCode,
            flagged
        );

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            PaymentAuditAction.SaaSPaymentRefundRejected,
            request.RequestedByUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                RefundRequestId = request.Id,
                RefundAmountCents = request.Amount.AmountCents,
                RejectionCode = rejectionCode,
                FlaggedForReview = flagged,
            },
            reason: request.Reason,
            nowUtc,
            ct
        );
    }
}
