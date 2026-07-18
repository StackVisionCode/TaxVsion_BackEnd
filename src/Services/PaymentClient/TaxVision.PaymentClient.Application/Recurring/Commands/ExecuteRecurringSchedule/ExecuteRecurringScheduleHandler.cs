using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;

namespace TaxVision.PaymentClient.Application.Recurring.Commands.ExecuteRecurringSchedule;

/// <summary>
/// Reusa el mismo pipeline de cobro que el endpoint backend-driven (§E.6/G.4) — despacha un
/// <see cref="ChargeTenantPaymentCommand"/> con <c>TenantRecurringPayment.PaymentMethodReference</c>
/// (el método off-session tokenizado una sola vez al crear el plan), así que Connect vs
/// DirectApiKeys, split de fee y auditoría del <c>TenantPayment</c> ya salen gratis de ahí —
/// este handler solo traduce el resultado a la máquina de estados del <c>RecurringSchedule</c>.
/// </summary>
public static class ExecuteRecurringScheduleHandler
{
    public static async Task<Result> Handle(
        ExecuteRecurringScheduleCommand command,
        ITenantRecurringPaymentRepository plans,
        ITenantPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdAsync(command.TenantRecurringPaymentId, command.TenantId, ct);
        if (plan is null)
            return Result.Failure(
                new Error("TenantRecurringPayment.NotFound", "TenantRecurringPayment does not exist.")
            );

        var nowUtc = DateTime.UtcNow;
        var processingResult = plan.MarkScheduleProcessing(command.ScheduleId, Guid.Empty, nowUtc);
        if (processingResult.IsFailure)
            return processingResult;

        var idempotencyKey = $"recurring:{plan.Id:N}:{command.ScheduleId:N}";
        var chargeResult = await bus.InvokeAsync<Result<Guid>>(
            new ChargeTenantPaymentCommand(
                plan.TenantId,
                plan.ProviderCode,
                plan.Amount.AmountCents,
                plan.Amount.Currency,
                plan.TaxpayerId,
                plan.Purpose.Kind,
                plan.Purpose.ExternalReferenceId,
                plan.PaymentMethodReference,
                ReceiptEmail: null,
                idempotencyKey,
                ActorUserId: Guid.Empty,
                plan.PlatformFeeAmountCents,
                plan.PlatformFeeReference
            ),
            ct
        );

        if (chargeResult.IsFailure)
        {
            var directFailureResult = plan.RecordFailure(
                command.ScheduleId,
                chargeResult.Error.Message,
                Guid.Empty,
                nowUtc
            );
            if (directFailureResult.IsFailure)
                return directFailureResult;

            await AuditFailureAsync(audit, plan, correlation, nowUtc, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var payment = await payments.GetByIdAsync(chargeResult.Value, plan.TenantId, ct);
        var succeeded = payment?.Status == PaymentStatus.Succeeded;

        var applyResult = succeeded
            ? plan.RecordSuccess(command.ScheduleId, chargeResult.Value, payment!.Status.ToString(), Guid.Empty, nowUtc)
            : plan.RecordFailure(
                command.ScheduleId,
                payment?.FailureReason ?? payment?.Status.ToString(),
                Guid.Empty,
                nowUtc
            );
        if (applyResult.IsFailure)
            return applyResult;

        // Mantiene el plan auto-alimentado: si ya no quedan schedules pendientes/en retry por
        // delante, genera el siguiente — así el plan avanza indefinidamente sin un job de
        // "top up" aparte.
        if (
            plan.Status == RecurringStatus.Active
            && !plan.Schedules.Any(s =>
                s.Status is RecurringScheduleStatus.Pending or RecurringScheduleStatus.RetryPending
            )
        )
            plan.GenerateSchedules(1, nowUtc);

        await AuditEntryFactory.AppendAsync(
            audit,
            plan.TenantId,
            nameof(TenantRecurringPayment),
            plan.Id,
            succeeded
                ? PaymentAuditAction.TenantRecurringPaymentExecutionSucceeded
                : PaymentAuditAction.TenantRecurringPaymentExecutionFailed,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                command.ScheduleId,
                plan.Status,
                Succeeded = succeeded,
            },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static async Task AuditFailureAsync(
        IPaymentAuditLogWriter audit,
        TenantRecurringPayment plan,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    ) =>
        await AuditEntryFactory.AppendAsync(
            audit,
            plan.TenantId,
            nameof(TenantRecurringPayment),
            plan.Id,
            PaymentAuditAction.TenantRecurringPaymentExecutionFailed,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new { plan.Status },
            reason: null,
            nowUtc,
            ct
        );
}
