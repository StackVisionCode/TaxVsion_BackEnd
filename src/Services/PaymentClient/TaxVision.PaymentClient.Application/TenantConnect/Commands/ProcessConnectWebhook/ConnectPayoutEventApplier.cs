using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.Payouts;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;

/// <summary>Eventos <c>payout.paid</c> y <c>payout.failed</c>. Devuelve lo que hay que publicar después de guardar.</summary>
internal static class ConnectPayoutEventApplier
{
    public static async Task<Result<IReadOnlyList<object>>> ApplyAsync(
        ConnectWebhookEvent evt,
        TenantConnectAccount account,
        IPayoutScheduleRepository payoutSchedules,
        IPaymentAuditLogWriter audit,
        ICorrelationContext correlation,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var schedule = await payoutSchedules.GetByTenantConnectAccountIdAsync(account.Id, ct);
        if (schedule is null)
            return Result.Failure<IReadOnlyList<object>>(
                new Error("PayoutSchedule.NotFound", "No PayoutSchedule exists for this Connect account yet.")
            );

        var amountResult = Money.Create(evt.PayoutAmountCents ?? 0, evt.PayoutCurrency ?? schedule.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<IReadOnlyList<object>>(amountResult.Error);

        var failed = evt.EventType == "payout.failed";
        if (failed)
            schedule.RecordPayoutFailed(
                evt.PayoutReference!,
                amountResult.Value,
                evt.PayoutFailureReason ?? "Payout failed.",
                nowUtc
            );
        else
            schedule.RecordPayoutPaid(evt.PayoutReference!, amountResult.Value, nowUtc);

        await AuditEntryFactory.AppendAsync(
            audit,
            account.TenantId,
            nameof(PayoutSchedule),
            schedule.Id,
            PaymentAuditAction.PayoutScheduleUpdated,
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                evt.PayoutReference,
                evt.PayoutAmountCents,
                Failed = failed,
            },
            reason: null,
            nowUtc,
            ct
        );

        object message = failed
            ? new PayoutFailedIntegrationEvent
            {
                TenantId = account.TenantId,
                PayoutScheduleId = schedule.Id,
                ProviderPayoutReference = evt.PayoutReference!,
                AmountCents = amountResult.Value.AmountCents,
                Currency = amountResult.Value.Currency,
                FailureReason = evt.PayoutFailureReason ?? "Payout failed.",
                FailedAtUtc = nowUtc,
                CorrelationId = correlation.CorrelationId,
            }
            : new PayoutCompletedIntegrationEvent
            {
                TenantId = account.TenantId,
                PayoutScheduleId = schedule.Id,
                ProviderPayoutReference = evt.PayoutReference!,
                AmountCents = amountResult.Value.AmountCents,
                Currency = amountResult.Value.Currency,
                PaidAtUtc = nowUtc,
                CorrelationId = correlation.CorrelationId,
            };
        return Result.Success<IReadOnlyList<object>>([message]);
    }
}
