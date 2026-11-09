using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;

/// <summary>Tercer paso del webhook: lleva el payload del provider a la transición del pago.</summary>
internal static class WebhookPayloadApplier
{
    public static Result Apply(SaaSPayment payment, WebhookEventPayload payload, IPaymentAppMetrics metrics)
    {
        var nowUtc = DateTime.UtcNow;

        switch (payload.Status)
        {
            case PaymentStatus.Processing:
                return Result.Success();

            case PaymentStatus.Succeeded:
                return payment.ApplyProviderSuccess(payload.PaidAmountCents, payload.PaidCurrency, nowUtc);

            case PaymentStatus.Failed:
                return payment.ApplyProviderFailure(payload.FailureCode, payload.FailureMessage, nowUtc);

            case PaymentStatus.Cancelled:
                return payment.CancelByAdmin("ProviderCancelled", Guid.Empty, nowUtc);

            case PaymentStatus.Refunded when payload.RefundedAmountCents is { } refundedCents:
                var refund = payment.ApplyProviderRefundTotal(refundedCents, nowUtc);
                if (refund.IsFailure)
                    return Result.Failure(refund.Error);
                if (refund.Value)
                    metrics.RecordRefunded(payment.ProviderCode.ToString());
                return Result.Success();

            case PaymentStatus.ChargedBack:
                var chargedBack = payment.MarkChargedBack(
                    nowUtc,
                    payload.FailureMessage ?? "Chargeback dispute created.",
                    Guid.Empty
                );
                if (chargedBack.IsFailure)
                    return chargedBack;
                metrics.RecordChargedBack(payment.ProviderCode.ToString());
                return Result.Success();

            default:
                return Result.Failure(
                    new Error(
                        "WebhookEvent.UnsupportedPaymentStatus",
                        $"Payment status {payload.Status} is not actionable."
                    )
                );
        }
    }

    /// <summary>El payload no se aplicó: un éxito del proveedor queda para revisión; lo demás, Stale.</summary>
    public static async Task RecordNotAppliedAsync(
        SaaSPayment payment,
        WebhookEvent webhookEvent,
        WebhookEventPayload payload,
        Error transitionError,
        IPaymentAuditLogWriter audit,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var reviewReason = LateProviderSuccessPolicy.ReviewReasonFor(payment, payload.Status, transitionError);
        if (reviewReason is not null)
        {
            await FlagForReviewAsync(
                payment,
                webhookEvent,
                payload,
                reviewReason,
                transitionError.Code,
                audit,
                metrics,
                correlation,
                logger,
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        webhookEvent.MarkStale(payment.Id, transitionError.Code, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation(
            "{Provider} webhook {EventType} ({ProviderEventId}) is stale for SaaSPayment {SaaSPaymentId}: {ErrorCode}.",
            webhookEvent.ProviderCode,
            webhookEvent.EventType,
            webhookEvent.ProviderEventId,
            payment.Id,
            transitionError.Code
        );
    }

    // Éxito del proveedor que no se aplica: queda visible para revisión en vez de Stale silencioso.
    private static async Task FlagForReviewAsync(
        SaaSPayment payment,
        WebhookEvent webhookEvent,
        WebhookEventPayload payload,
        string reviewReason,
        string errorCode,
        IPaymentAuditLogWriter audit,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var firstFlag = LateProviderSuccessPolicy.Flag(
            payment,
            reviewReason,
            payload.ReconciledChargeReference ?? payload.ProviderChargeReference,
            payload.PaidAmountCents,
            payload.PaidCurrency,
            metrics,
            logger,
            nowUtc
        );
        webhookEvent.MarkNeedsReview(payment.Id, errorCode, nowUtc);

        if (!firstFlag)
            return;

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            PaymentAuditAction.SaaSPaymentReviewRequired,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                payment.ReviewReasonCode,
                payment.ReviewProviderReference,
                payment.ReviewPaidAmountCents,
                payment.ReviewPaidCurrency,
                webhookEvent.ProviderEventId,
                webhookEvent.EventType,
            },
            reason: errorCode,
            nowUtc,
            ct
        );
    }

    public static void ReconcileProviderReference(
        PaymentProviderCode provider,
        string providerEventId,
        WebhookEventPayload payload,
        SaaSPayment payment,
        ILogger logger,
        DateTime nowUtc
    )
    {
        if (
            payload.ReconciledChargeReference is not { } reconciledRaw
            || reconciledRaw == payment.ExternalChargeReference?.Value
        )
            return;

        var reconciledReferenceResult = ExternalPaymentReference.Create(provider, reconciledRaw);
        if (reconciledReferenceResult.IsFailure)
        {
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} carried an invalid reconciled charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                provider,
                providerEventId,
                payment.Id,
                reconciledReferenceResult.Error.Code,
                reconciledReferenceResult.Error.Message
            );
            return;
        }

        var reconcileResult = payment.ReconcileProviderChargeReference(reconciledReferenceResult.Value, nowUtc);
        if (reconcileResult.IsFailure)
            logger.LogWarning(
                "{Provider} webhook {ProviderEventId} could not reconcile the charge reference for SaaSPayment {SaaSPaymentId}: {ErrorCode}: {ErrorMessage}",
                provider,
                providerEventId,
                payment.Id,
                reconcileResult.Error.Code,
                reconcileResult.Error.Message
            );
    }

    public static PaymentAuditAction AuditActionFor(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Succeeded => PaymentAuditAction.SaaSPaymentSucceeded,
            PaymentStatus.Failed => PaymentAuditAction.SaaSPaymentFailed,
            PaymentStatus.Cancelled => PaymentAuditAction.SaaSPaymentCancelled,
            PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded => PaymentAuditAction.SaaSPaymentRefundedPartial,
            PaymentStatus.ChargedBack => PaymentAuditAction.SaaSPaymentChargedBack,
            _ => PaymentAuditAction.SaaSPaymentMarkedProcessing,
        };
}
