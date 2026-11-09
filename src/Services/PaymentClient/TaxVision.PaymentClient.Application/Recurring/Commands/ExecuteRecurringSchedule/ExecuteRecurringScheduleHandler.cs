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

        // Un cobro anterior de este intento sigue sin resultado: una clave nueva podría cobrar dos veces. Espera a la
        // reconciliación, y la próxima vuelta lee lo que quedó con la misma clave.
        var scheduleToCharge = plan.Schedules.SingleOrDefault(s => s.Id == command.ScheduleId);
        if (
            scheduleToCharge is not null
            && IsWaitingForTheProvider(
                await payments.GetByIdempotencyKeyAsync(
                    plan.TenantId,
                    scheduleToCharge.ChargeIdempotencyKey(plan.Id),
                    ct
                )
            )
        )
            return Result.Success();

        var nowUtc = DateTime.UtcNow;
        var processingResult = plan.MarkScheduleProcessing(command.ScheduleId, Guid.Empty, nowUtc);
        if (processingResult.IsFailure)
            return processingResult;

        var schedule = plan.Schedules.Single(s => s.Id == command.ScheduleId);
        var idempotencyKey = schedule.ChargeIdempotencyKey(plan.Id);
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

        // Sin resultado final: se revierte el "en proceso" del schedule para no reintentar con otra clave. El webhook o
        // la reconciliación cierran el pago, y la próxima vuelta lee lo que quedó con la misma clave.
        if (IsWaitingForTheProvider(payment))
            throw new InvalidOperationException(
                $"TenantPayment {payment!.Id} has no final result yet; recurring schedule {command.ScheduleId} stays as it was."
            );

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

    // D-PAY-13: Processing y RequiresAction no son fallos: el cargo sigue abierto en el proveedor.
    private static bool IsWaitingForTheProvider(TenantPayment? payment) =>
        payment?.Status is PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.RequiresAction;

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
