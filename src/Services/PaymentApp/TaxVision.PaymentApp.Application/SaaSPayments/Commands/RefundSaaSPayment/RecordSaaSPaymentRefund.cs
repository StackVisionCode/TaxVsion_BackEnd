using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>
/// Paso 3 del reembolso (BB-8): registra lo que respondió el proveedor en otra transacción.
/// <list type="bullet">
/// <item>Sin <paramref name="RejectionCode"/>: el proveedor reembolsó. Si el dominio no lo acepta, la intención queda en
/// conciliación (no se reenvía) y el pago, en revisión.</item>
/// <item><paramref name="Unrecorded"/>: no se sabe si reembolsó y no se puede repetir. Va a conciliación.</item>
/// <item>Con <paramref name="RejectionCode"/>: rechazo definitivo, sin dinero movido. Se audita y, si
/// <paramref name="FlagRejectionForReview"/>, el pago entra a la bandeja.</item>
/// </list>
/// </summary>
public sealed record RecordSaaSPaymentRefundCommand(
    Guid SaaSPaymentId,
    Guid RequestId,
    string? ProviderRefundReference,
    string? RejectionCode = null,
    bool FlagRejectionForReview = false,
    bool Unrecorded = false
);

public enum SaaSPaymentRefundRecordKind
{
    Recorded = 1,
    Rejected = 2,
    NeedsReconciliation = 3,
}

/// <param name="VisibleForReview">El pago está en la bandeja de revisión sin resolver. False con un rechazo o una
/// conciliación pendiente quiere decir que nadie lo va a ver allí.</param>
public sealed record SaaSPaymentRefundRecord(SaaSPaymentRefundRecordKind Kind, bool VisibleForReview);

public static class RecordSaaSPaymentRefundHandler
{
    public static async Task<Result<SaaSPaymentRefundRecord>> Handle(
        RecordSaaSPaymentRefundCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, ct);
        var request = payment?.RefundRequests.FirstOrDefault(candidate => candidate.Id == command.RequestId);
        if (payment is null || request is null)
            return Result.Failure<SaaSPaymentRefundRecord>(
                new Error("SaaSPayment.RefundRequestNotFound", "The refund request does not exist.")
            );

        // Un reintento después de registrar: devuelve lo que ya quedó.
        if (request.Status != RefundRequestStatus.Pending)
            return Result.Success(new SaaSPaymentRefundRecord(KindOf(request.Status), IsVisibleForReview(payment)));

        var nowUtc = DateTime.UtcNow;
        SaaSPaymentRefundRecord record;
        if (command.RejectionCode is { } rejectionCode)
        {
            await RefundRejection.MarkAsync(
                payment,
                request,
                rejectionCode,
                command.FlagRejectionForReview,
                audit,
                metrics,
                correlation,
                logger,
                nowUtc,
                ct
            );
            record = new(SaaSPaymentRefundRecordKind.Rejected, IsVisibleForReview(payment));
        }
        else if (command.Unrecorded)
            record = await ReconcileAsync(
                payment,
                request,
                command,
                "SaaSPayment.RefundOutcomeUnknown",
                audit,
                metrics,
                correlation,
                logger,
                nowUtc,
                ct
            );
        else
        {
            var completed = payment.CompleteRefundRequest(request.Id, command.ProviderRefundReference, nowUtc);
            record = completed.IsSuccess
                ? await RecordedAsync(payment, request, completed.Value, audit, metrics, correlation, nowUtc, ct)
                : await ReconcileAsync(
                    payment,
                    request,
                    command,
                    completed.Error.Code,
                    audit,
                    metrics,
                    correlation,
                    logger,
                    nowUtc,
                    ct
                );
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(record);
    }

    private static async Task<SaaSPaymentRefundRecord> RecordedAsync(
        SaaSPayment payment,
        RefundRequest request,
        bool addedLine,
        IPaymentAuditLogWriter audit,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        metrics.RecordRefunded(payment.ProviderCode.ToString());
        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            payment.Status == PaymentStatus.Refunded
                ? PaymentAuditAction.SaaSPaymentRefundedFull
                : PaymentAuditAction.SaaSPaymentRefundedPartial,
            request.RequestedByUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                RefundedCents = request.Amount.AmountCents,
                RefundRequestId = request.Id,
                request.ProviderRefundReference,
                // false: la sincronización del proveedor ya lo había registrado.
                AddedRefundLine = addedLine,
            },
            reason: request.Reason,
            nowUtc,
            ct
        );
        return new SaaSPaymentRefundRecord(SaaSPaymentRefundRecordKind.Recorded, IsVisibleForReview(payment));
    }

    private static async Task<SaaSPaymentRefundRecord> ReconcileAsync(
        SaaSPayment payment,
        RefundRequest request,
        RecordSaaSPaymentRefundCommand command,
        string failureCode,
        IPaymentAuditLogWriter audit,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        await RefundReconciliation.MarkAsync(
            payment,
            request.Id,
            command.ProviderRefundReference,
            failureCode,
            audit,
            metrics,
            correlation,
            logger,
            nowUtc,
            ct
        );
        return new SaaSPaymentRefundRecord(
            SaaSPaymentRefundRecordKind.NeedsReconciliation,
            IsVisibleForReview(payment)
        );
    }

    internal static bool IsVisibleForReview(SaaSPayment payment) =>
        payment.ReviewRequiredAtUtc is not null && payment.ReviewResolvedAtUtc is null;

    private static SaaSPaymentRefundRecordKind KindOf(RefundRequestStatus status) =>
        status switch
        {
            RefundRequestStatus.Completed => SaaSPaymentRefundRecordKind.Recorded,
            RefundRequestStatus.Rejected => SaaSPaymentRefundRecordKind.Rejected,
            _ => SaaSPaymentRefundRecordKind.NeedsReconciliation,
        };
}
