using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RecordSaaSPaymentCharge;

/// <summary>Paso 3 del cobro (patrón BB-8): aplica lo que respondió el provider en su propia transacción. Trae uno
/// solo de <paramref name="Authorization"/>, <paramref name="OffSession"/> y <paramref name="Failure"/>.</summary>
public sealed record RecordSaaSPaymentChargeCommand(
    Guid TenantId,
    Guid SaaSPaymentId,
    SaaSPaymentChargeStrategy Strategy,
    ChargeAuthorizationResult? Authorization,
    OffSessionChargeOutcome? OffSession,
    Error? Failure,
    Guid ActorUserId,
    string? CorrelationId
);

public static class RecordSaaSPaymentChargeHandler
{
    public static async Task<Result> Handle(
        RecordSaaSPaymentChargeCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        // Otro camino lo cerró mientras se cobraba. Si el provider sí cobró, es dinero sin registro: a revisión.
        if (payment.Status != PaymentStatus.Pending)
        {
            if (FlagIfChargedAfterClose(payment, command, metrics, logger))
                await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        // D-PAY-13: sigue Pending con el intento grabado y lo resuelve PendingChargeReconciliationJob.
        if (command.Authorization is { OutcomeUnknown: true } unknown)
        {
            logger.LogWarning(
                "SaaSPayment {SaaSPaymentId} charge has no known outcome ({FailureCode}); left for reconciliation.",
                payment.Id,
                unknown.FailureCode
            );
            return Result.Success();
        }

        var attempted = payment.Attempts.Count;
        Apply(payment, command, metrics, logger);

        if (command.Strategy == SaaSPaymentChargeStrategy.Authorize)
            await AuditEntryFactory.AppendAsync(
                audit,
                payment.TenantId,
                nameof(SaaSPayment),
                payment.Id,
                SaaSPaymentChargeOutcome.MapAuditAction(payment.Status),
                command.ActorUserId,
                correlation.CorrelationId,
                before: (object?)null,
                after: new
                {
                    payment.Status,
                    payment.FailureCode,
                    payment.FailureReason,
                    RetryAttempt = attempted > 0 ? payment.Attempts.Count : (int?)null,
                },
                reason: null,
                DateTime.UtcNow,
                ct
            );

        // El resultado (succeeded/failed) lo avisa SaaSPaymentDomainEventHandler con el commit.
        await unitOfWork.SaveChangesAsync(ct);

        // Correo del cobro recurrente: viaja por el canal de status-changed (no mueve el estado). El de fallo lo
        // manda Subscription en cada intento fallido. Guardar antes de publicar.
        if (command.OffSession?.Status == OffSessionChargeStatus.Succeeded)
            await PublishRecurringChargeSucceededEmailAsync(payment, command, bus);

        logger.LogInformation(
            "SaaSPayment {SaaSPaymentId} for tenant {TenantId} finished with status {Status} (attempt {Attempt}).",
            payment.Id,
            payment.TenantId,
            payment.Status,
            payment.Attempts.Count
        );

        return Result.Success();
    }

    private static void Apply(
        SaaSPayment payment,
        RecordSaaSPaymentChargeCommand command,
        IPaymentAppMetrics metrics,
        ILogger logger
    )
    {
        var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, DateTime.UtcNow);

        if (command.Failure is { } failure)
            SaaSPaymentChargeOutcome.FailPayment(payment, failure, command.ActorUserId, nextRetryAtUtc, metrics);
        else if (command.OffSession is { } offSession)
            SaaSPaymentOffSessionOutcome.Apply(payment, offSession, payment.ProviderCode, metrics, logger);
        else
            SaaSPaymentChargeOutcome.ApplyChargeOutcome(
                payment,
                command.Authorization!,
                command.ActorUserId,
                nextRetryAtUtc,
                metrics
            );
    }

    private static bool FlagIfChargedAfterClose(
        SaaSPayment payment,
        RecordSaaSPaymentChargeCommand command,
        IPaymentAppMetrics metrics,
        ILogger logger
    )
    {
        var succeeded =
            command.Authorization?.Status == PaymentStatus.Succeeded
            || command.OffSession?.Status == OffSessionChargeStatus.Succeeded;
        if (!succeeded || !LateProviderSuccessPolicy.IsClosedWithoutCharge(payment.Status))
            return false;

        return LateProviderSuccessPolicy.Flag(
            payment,
            SaaSPayment.ReviewReasonLateProviderSuccess,
            command.Authorization?.ProviderChargeReference ?? command.OffSession?.ProviderPaymentReference,
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            metrics,
            logger,
            DateTime.UtcNow
        );
    }

    private static async Task PublishRecurringChargeSucceededEmailAsync(
        SaaSPayment payment,
        RecordSaaSPaymentChargeCommand command,
        IMessageBus bus
    ) =>
        await bus.PublishAsync(
            new TenantSubscriptionStatusChangedIntegrationEvent
            {
                TenantId = payment.TenantId,
                CorrelationId = command.CorrelationId,
                TenantSubscriptionId = payment.TargetAggregateId,
                Status = "Active",
                PreviousStatus = "Active",
                Reason = nameof(SubscriptionChangeReason.RecurringChargeSucceeded),
                Amount = (payment.Amount.AmountCents / 100m).ToString(
                    "F2",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                DateCharged = DateTime.UtcNow.ToString(
                    "MMMM d, yyyy",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            }
        );
}
