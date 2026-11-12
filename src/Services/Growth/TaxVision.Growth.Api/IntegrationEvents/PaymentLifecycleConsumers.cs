using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Reservations.CancelReservation;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Application.Rewards.ClawbackWalletCreditReward;
using TaxVision.Referrals.Domain.Programs;
using Wolverine;

namespace TaxVision.Growth.Api.IntegrationEvents;

/// <summary>
/// Host-level orchestration between the independently modeled Codes and Referrals
/// bounded contexts. Payment remains the financial authority; these consumers only
/// translate an authoritative financial fact into idempotent local commands.
/// </summary>
public static class PaymentSucceededConsumer
{
    private static readonly Guid PaymentServiceActorId = new("51000000-0000-0000-0000-000000000001");

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

        if (message.ReferralAttributionId is { } attributionId)
        {
            var paymentSource = ParsePaymentSource(message.PaymentSource);
            var qualification = await bus.InvokeAsync<Result<QualifyReferralResult>>(
                new QualifyReferralCommand(
                    message.TenantId,
                    attributionId,
                    message.EventId,
                    message.PaymentId,
                    paymentSource,
                    message.NetAmountCents,
                    message.Currency,
                    message.IsFirstSuccessfulPayment,
                    message.PaidAtUtc,
                    eventKey,
                    PaymentServiceActorId
                ),
                ct
            );
            EnsureApplied(qualification);
        }
    }

    private static QualifyingPaymentSource ParsePaymentSource(string paymentSource) =>
        Enum.TryParse<QualifyingPaymentSource>(paymentSource, ignoreCase: true, out var parsed)
            ? parsed
            : throw GrowthErrorClassification.ToException(
                GrowthErrorClassification.InvalidPaymentSourceCode,
                $"Payment source '{paymentSource}' is not supported."
            );

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
/// revierte en su Wallet. Un reembolso parcial no lo toca. Idempotente: reentregar el evento es seguro.
/// </summary>
public static class PaymentRefundedConsumer
{
    public static Task Handle(PaymentRefundedIntegrationEvent message, IMessageBus bus, CancellationToken ct) =>
        PaymentRewardClawback.ApplyAsync(
            bus,
            message.PaymentId,
            message.CumulativeRefundedAmountCents,
            $"The referred payment was refunded ({message.RefundReference}).",
            ct
        );
}

/// <summary>
/// Chargeback: el proveedor retira los fondos al abrir la disputa, así que equivale a un reembolso total salvo que la
/// disputa se GANE (en ese caso el premio se mantiene).
/// </summary>
public static class PaymentChargebackChangedConsumer
{
    public static Task Handle(
        PaymentChargebackChangedIntegrationEvent message,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        !string.Equals(message.DisputeStatus, "won", StringComparison.OrdinalIgnoreCase)
            ? PaymentRewardClawback.ApplyAsync(
                bus,
                message.PaymentId,
                refundedCents: null,
                $"The referred payment was charged back (dispute {message.DisputeId}).",
                ct
            )
            : Task.CompletedTask;
}

internal static class PaymentRewardClawback
{
    private static readonly Guid PaymentServiceActorId = new("51000000-0000-0000-0000-000000000001");

    public static async Task ApplyAsync(
        IMessageBus bus,
        Guid paymentId,
        long? refundedCents,
        string reason,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<ClawbackWalletCreditRewardResult>>(
            new ClawbackWalletCreditRewardCommand(paymentId, refundedCents, reason, PaymentServiceActorId),
            ct
        );
        if (result.IsFailure)
            throw GrowthErrorClassification.ToException(result.Error);

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
