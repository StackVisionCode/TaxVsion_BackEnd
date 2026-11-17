using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Compensations.CompensateRedemption;
using TaxVision.Codes.Application.Compensations.FindRedemptionByReservation;
using TaxVision.Codes.Application.Reservations.CancelReservation;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Application.Rewards.DisputeHold;
using Wolverine;

namespace TaxVision.Growth.Api.IntegrationEvents;

/// <summary>
/// Host-level orchestration between the independently modeled Codes and Referrals
/// bounded contexts. Payment remains the financial authority; these consumers only
/// translate an authoritative financial fact into idempotent local commands.
/// </summary>
public static class PaymentSucceededConsumer
{
    public static async Task Handle(PaymentSucceededIntegrationEvent message, IMessageBus bus, CancellationToken ct)
    {
        var eventKey = $"event:{message.EventId:N}";

        if (message.CodeReservationId is { } reservationId)
        {
            if (string.IsNullOrWhiteSpace(message.PromotionSnapshotHash))
            {
                throw GrowthErrorClassification.ToException(
                    GrowthErrorClassification.SnapshotRequiredCode,
                    "A code reservation success must include PromotionSnapshotHash."
                );
            }

            var commit = await bus.InvokeAsync<Result<CommitReservationResponse>>(
                new CommitReservationCommand(
                    message.TenantId,
                    reservationId,
                    message.PaymentSource,
                    message.PaymentId,
                    message.PromotionSnapshotHash,
                    message.EventId,
                    eventKey
                ),
                ct
            );
            EnsureApplied(commit);
        }
    }

    private static void EnsureApplied<T>(Result<T> result)
    {
        if (result.IsFailure)
            throw GrowthErrorClassification.ToException(result.Error);
    }
}

public static class PaymentFailedConsumer
{
    public static Task Handle(PaymentFailedIntegrationEvent message, IMessageBus bus, CancellationToken ct) =>
        message.IsTerminal
            ? PaymentReservationCancellation.CancelAsync(message, message.FailureCode, bus, ct)
            : Task.CompletedTask;
}

public static class PaymentCancelledConsumer
{
    public static Task Handle(PaymentCancelledIntegrationEvent message, IMessageBus bus, CancellationToken ct) =>
        PaymentReservationCancellation.CancelAsync(message, message.ReasonCode, bus, ct);
}

/// <summary>
/// Reembolso del pago del REFERIDO: si fue total, el premio del referidor se cancela (aún no acreditado) o se
/// revierte en su Wallet. Un reembolso parcial no lo toca. Además compensa cada código de descuento del pago: total →
/// el código vuelve a estar disponible; parcial → queda gastado. Idempotente: reentregar el evento es seguro.
/// </summary>
public static class PaymentRefundedConsumer
{
    public static async Task Handle(
        PaymentRefundedIntegrationEvent message,
        IMessageBus bus,
        ILogger<PaymentRefundedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var reason = $"The payment was refunded ({message.RefundReference}).";
        await PaymentRewardClawback.ApplyAsync(
            bus,
            message.PaymentId,
            message.CumulativeRefundedAmountCents,
            $"The referred payment was refunded ({message.RefundReference}).",
            "ReferredPaymentRefunded",
            message.CorrelationId,
            ct
        );

        var fullyRefunded = message.CumulativeRefundedAmountCents >= message.NetAmountCents;
        await PaymentPromotionCompensation.ApplyAsync(
            bus,
            message,
            message.Promotions,
            fullyRefunded ? CodeCompensationType.RestoreAvailability : CodeCompensationType.KeepConsumed,
            "refund",
            reason,
            logger,
            ct
        );
    }
}

/// <summary>
/// Disputa del pago: abierta → el premio aún sin acreditar se retiene; perdida → se quita el premio y el código queda
/// gastado (como un reembolso total); ganada → se libera el premio retenido. El código no se toca mientras está abierta.
/// </summary>
public static class PaymentChargebackChangedConsumer
{
    private static readonly Guid PaymentServiceActorId = new("51000000-0000-0000-0000-000000000001");

    public static async Task Handle(
        PaymentChargebackChangedIntegrationEvent message,
        IMessageBus bus,
        ILogger<PaymentChargebackChangedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        switch (message.DisputeStatus.ToLowerInvariant())
        {
            case "opened":
                await InvokeAsync(
                    bus,
                    new HoldReferralRewardForDisputeCommand(
                        message.PaymentId,
                        message.DisputeId,
                        PaymentServiceActorId
                    ),
                    ct
                );
                return;

            case "won":
                await InvokeAsync(
                    bus,
                    new ReleaseReferralRewardDisputeHoldCommand(
                        message.PaymentId,
                        message.DisputeId,
                        PaymentServiceActorId
                    ),
                    ct
                );
                return;

            case "lost":
                await PaymentRewardClawback.ApplyAsync(
                    bus,
                    message.PaymentId,
                    refundedCents: null,
                    $"The referred payment lost its dispute ({message.DisputeId}).",
                    "ReferredPaymentDisputeLost",
                    message.CorrelationId,
                    ct
                );
                await PaymentPromotionCompensation.ApplyAsync(
                    bus,
                    message,
                    message.Promotions,
                    CodeCompensationType.KeepConsumed,
                    "chargeback",
                    $"The payment lost its dispute ({message.DisputeId}).",
                    logger,
                    ct
                );
                return;

            default:
                logger.LogWarning(
                    "Dispute {DisputeId} of payment {PaymentId} has an unknown status {DisputeStatus}; ignored.",
                    message.DisputeId,
                    message.PaymentId,
                    message.DisputeStatus
                );
                return;
        }
    }

    private static async Task InvokeAsync(IMessageBus bus, object command, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ReferralRewardDisputeHoldOutcome>>(command, ct);
        if (result.IsFailure)
            throw GrowthErrorClassification.ToException(result.Error);
    }
}

/// <summary>
/// Compensa el canje de cada código de descuento del pago. Un código cuya reserva no se confirmó, o que ya estaba
/// compensado, no falla el evento: se registra y sigue. La clave es por evento y reserva, así que reentregar no duplica.
/// </summary>
internal static class PaymentPromotionCompensation
{
    public static async Task ApplyAsync(
        IMessageBus bus,
        PaymentLifecycleIntegrationEvent message,
        IReadOnlyList<PaymentPromotionData>? promotions,
        CodeCompensationType type,
        string kind,
        string reason,
        ILogger logger,
        CancellationToken ct
    )
    {
        foreach (var promotion in promotions ?? [])
        {
            var redemption = await bus.InvokeAsync<Result<RedemptionToCompensate?>>(
                new FindRedemptionByReservationQuery(message.TenantId, promotion.CodeReservationId),
                ct
            );
            if (redemption.IsFailure || redemption.Value is not { } found)
                continue; // la reserva no llegó a confirmarse: no hay canje que compensar

            var compensated = await bus.InvokeAsync<Result<CompensateRedemptionResponse>>(
                new CompensateRedemptionCommand(
                    message.TenantId,
                    found.RedemptionId,
                    type,
                    type == CodeCompensationType.RestoreAvailability ? found.DiscountAmountCents : 0,
                    found.Currency,
                    reason,
                    message.EventId,
                    $"{kind}:{message.EventId:N}:{promotion.CodeReservationId:N}"
                ),
                ct
            );
            // Un fallo de negocio (ya compensado, otro evento) no se reintenta, pero queda a la vista.
            if (compensated.IsFailure)
                logger.LogWarning(
                    "Code redemption {RedemptionId} of payment {PaymentId} was not compensated: {Code}.",
                    found.RedemptionId,
                    message.PaymentId,
                    compensated.Error.Code
                );
        }
    }
}

internal static class PaymentRewardClawback
{
    private static readonly Guid PaymentServiceActorId = new("51000000-0000-0000-0000-000000000001");

    public static async Task ApplyAsync(
        IMessageBus bus,
        Guid paymentId,
        long? refundedCents,
        string reason,
        string cancelledReasonCode,
        string correlationId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<ClawbackWalletCreditRewardResult>>(
            new ClawbackWalletCreditRewardCommand(paymentId, refundedCents, reason, PaymentServiceActorId),
            ct
        );
        if (result.IsFailure)
            throw GrowthErrorClassification.ToException(result.Error);

        // Se canceló antes de acreditarse: se le avisa al referidor. Si ya estaba acreditado, avisa la reversa.
        if (result.Value.Outcome == ClawbackWalletCreditRewardOutcome.Cancelled && result.Value.Reward is { } reward)
            await bus.PublishAsync(ReferralRewardEvents.Rejected(reward, cancelledReasonCode, correlationId));

        // El caso de uso devuelve la reversa a pedir (no publica); aquí se publica con el bus del consumer.
        if (result.Value.Request is { } request)
            await bus.PublishAsync(
                TaxVision.Growth.Infrastructure.Messaging.WalletCreditRequestEvents.ToEvent(request)
            );
    }
}

internal static class PaymentReservationCancellation
{
    public static async Task CancelAsync(
        PaymentLifecycleIntegrationEvent message,
        string reason,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (message.CodeReservationId is not { } reservationId)
            return;

        var safeReason =
            string.IsNullOrWhiteSpace(reason) ? "Payment did not complete."
            : reason.Trim().Length <= 500 ? reason.Trim()
            : reason.Trim()[..500];
        var result = await bus.InvokeAsync<Result<CancelReservationResponse>>(
            new CancelReservationCommand(
                message.TenantId,
                reservationId,
                message.PaymentSource,
                message.PaymentId,
                safeReason,
                $"event:{message.EventId:N}"
            ),
            ct
        );

        // A terminal payment failure can arrive after a success/commit. That stale
        // event must not reverse a committed redemption. Other failures remain
        // visible in the dead-letter queue, after retrying only the transient ones.
        if (result.IsFailure && result.Error.Code != "Codes.CodeReservation.InvalidTransition")
            throw GrowthErrorClassification.ToException(result.Error);
    }
}

public sealed class GrowthFinancialEventException(string errorCode, string message)
    : Exception($"{errorCode}: {message}")
{
    public string ErrorCode { get; } = errorCode;
}

/// <summary>Regla de negocio fija: reintentar no cambia el resultado. Va a la cola de errores sin reintentos.</summary>
public sealed class GrowthBusinessRuleException(string errorCode, string message) : Exception($"{errorCode}: {message}")
{
    public string ErrorCode { get; } = errorCode;
}
