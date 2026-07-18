using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

public static class RefundSaaSPaymentHandler
{
    public static async Task<Result> Handle(
        RefundSaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        IPaymentAttemptThrottle throttle,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct)
    {
        // Defensa en profundidad (§41.4) — 5 acciones admin de dinero por minuto por tenant es
        // más que suficiente para uso legítimo; por encima de eso probablemente sea un error
        // de automatización o una cuenta admin comprometida.
        if (await throttle.IsAdminActionThrottledAsync(command.TenantId, ct))
            return Result.Failure(new Error("PaymentApp.AdminActionThrottled", "Too many admin actions for this tenant in the last minute."));

        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        if (payment.ExternalChargeReference is null)
            return Result.Failure(new Error("SaaSPayment.NoChargeReference", "This payment was never charged with a provider — nothing to refund."));

        var amountResult = Money.Create(command.RefundAmountCents, payment.Amount.Currency);
        if (amountResult.IsFailure)
            return Result.Failure(amountResult.Error);

        var adapter = providerFactory.Resolve(payment.ProviderCode);
        var refundResult = await adapter.RefundAsync(payment.ExternalChargeReference.Value, amountResult.Value, command.Reason, ct);
        if (refundResult.IsFailure)
        {
            logger.LogWarning(
                "Provider refund failed for SaaSPayment {SaaSPaymentId}: {Error}", payment.Id, refundResult.Error.Message);
            return Result.Failure(refundResult.Error);
        }

        var applyResult = payment.RefundPartial(amountResult.Value, command.Reason, command.ActorUserId, DateTime.UtcNow);
        if (applyResult.IsFailure)
            return applyResult;

        metrics.RecordRefunded(payment.ProviderCode.ToString());
        await throttle.RegisterAdminActionAttemptAsync(command.TenantId, ct);

        await AuditEntryFactory.AppendAsync(
            audit, payment.TenantId, nameof(SaaSPayment), payment.Id, MapAuditAction(payment.Status),
            command.ActorUserId, correlation.CorrelationId,
            before: (object?)null,
            after: new { payment.Status, RefundedCents = command.RefundAmountCents },
            reason: command.Reason, DateTime.UtcNow, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "SaaSPayment {SaaSPaymentId} refunded {AmountCents} cents by {ActorUserId}; status now {Status}.",
            payment.Id, command.RefundAmountCents, command.ActorUserId, payment.Status);

        return Result.Success();
    }

    private static PaymentAuditAction MapAuditAction(PaymentStatus status) =>
        status == PaymentStatus.Refunded ? PaymentAuditAction.SaaSPaymentRefundedFull : PaymentAuditAction.SaaSPaymentRefundedPartial;
}
