using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Reservations.CancelReservation;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
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
    private static readonly Guid PaymentServiceActorId =
        new("51000000-0000-0000-0000-000000000001");

    public static async Task Handle(
        PaymentSucceededIntegrationEvent message,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var eventKey = $"event:{message.EventId:N}";

        if (message.CodeReservationId is { } reservationId)
        {
            if (string.IsNullOrWhiteSpace(message.PromotionSnapshotHash))
            {
                throw new GrowthFinancialEventException(
                    "Growth.PaymentEvent.SnapshotRequired",
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
            : throw new GrowthFinancialEventException(
                "Growth.PaymentEvent.InvalidSource",
                $"Payment source '{paymentSource}' is not supported."
            );

    private static void EnsureApplied<T>(Result<T> result)
    {
        if (result.IsFailure)
            throw new GrowthFinancialEventException(result.Error.Code, result.Error.Message);
    }
}

public static class PaymentFailedConsumer
{
    public static Task Handle(
        PaymentFailedIntegrationEvent message,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        message.IsTerminal
            ? PaymentReservationCancellation.CancelAsync(
                message,
                message.FailureCode,
                bus,
                ct
            )
            : Task.CompletedTask;
}

public static class PaymentCancelledConsumer
{
    public static Task Handle(
        PaymentCancelledIntegrationEvent message,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        PaymentReservationCancellation.CancelAsync(message, message.ReasonCode, bus, ct);
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

        var safeReason = string.IsNullOrWhiteSpace(reason)
            ? "Payment did not complete."
            : reason.Trim().Length <= 500
                ? reason.Trim()
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
        // retryable and eventually visible in the dead-letter queue.
        if (
            result.IsFailure
            && result.Error.Code != "Codes.CodeReservation.InvalidTransition"
        )
            throw new GrowthFinancialEventException(result.Error.Code, result.Error.Message);
    }
}

public sealed class GrowthFinancialEventException(string errorCode, string message)
    : Exception($"{errorCode}: {message}")
{
    public string ErrorCode { get; } = errorCode;
}
