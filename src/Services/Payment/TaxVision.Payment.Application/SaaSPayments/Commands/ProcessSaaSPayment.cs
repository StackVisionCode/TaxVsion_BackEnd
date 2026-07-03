using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;

namespace TaxVision.Payment.Application.SaaSPayments.Commands;

/// <summary>
/// Command dispatched by <c>WebhooksController</c> after receiving a verified Stripe webhook event
/// (<c>payment_intent.succeeded</c> or <c>payment_intent.payment_failed</c>).
/// Locates the corresponding <c>SaaSPayment</c> record and transitions its status to
/// <c>Completed</c> or <c>Failed</c> accordingly.
/// </summary>
public sealed record ProcessSaaSPaymentCommand(
    /// <summary>The Stripe PaymentIntent ID from the webhook event.</summary>
    string StripePaymentIntentId,
    /// <summary><c>true</c> if <c>payment_intent.succeeded</c>; <c>false</c> if payment failed.</summary>
    bool IsSuccess,
    /// <summary>Human-readable failure message from Stripe, or <c>null</c> on success.</summary>
    string? FailureReason);

/// <summary>
/// Wolverine handler for <see cref="ProcessSaaSPaymentCommand"/>.
/// Uses the static handler pattern consistent with the rest of TaxVision's Application layer.
/// </summary>
public static class ProcessSaaSPaymentHandler
{
    /// <summary>
    /// Finds the SaaS payment by <c>StripePaymentIntentId</c> and marks it completed or failed.
    /// Returns <c>Payment.NotFound</c> if no matching record exists (idempotent — Stripe may
    /// re-deliver webhooks; a missing record means the payment was never created).
    /// </summary>
    public static async Task<Result> Handle(
        ProcessSaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var payment = await payments.GetByStripePaymentIntentIdAsync(command.StripePaymentIntentId, ct);
        if (payment is null)
            return Result.Failure(new Error("Payment.NotFound",
                $"No SaaS payment found for intent {command.StripePaymentIntentId}."));

        if (command.IsSuccess)
            payment.MarkCompleted();
        else
            payment.MarkFailed(command.FailureReason ?? "Webhook reported failure.");

        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
