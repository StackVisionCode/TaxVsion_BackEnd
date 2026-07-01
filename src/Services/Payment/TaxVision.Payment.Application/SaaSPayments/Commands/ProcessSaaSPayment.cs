using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Application.SaaSPayments.Commands;

// Used internally by the webhook controller to update SaaSPayment status after Stripe webhook confirmation
public sealed record ProcessSaaSPaymentCommand(
    string StripePaymentIntentId,
    bool IsSuccess,
    string? FailureReason);

public static class ProcessSaaSPaymentHandler
{
    public static async Task<Result> Handle(
        ProcessSaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var payment = await FindByPaymentIntentIdAsync(payments, command.StripePaymentIntentId, ct);
        if (payment is null)
            return Result.Failure(new BuildingBlocks.Results.Error("Payment.NotFound", $"No SaaS payment found for intent {command.StripePaymentIntentId}."));

        if (command.IsSuccess)
            payment.MarkCompleted();
        else
            payment.MarkFailed(command.FailureReason ?? "Webhook reported failure.");

        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static async Task<SaaSPayment?> FindByPaymentIntentIdAsync(
        ISaaSPaymentRepository payments,
        string paymentIntentId,
        CancellationToken ct)
    {
        // The repository doesn't expose a direct query by StripePaymentIntentId,
        // so we rely on the webhook controller passing us enough context.
        // For now, this is a stub that would be extended with a dedicated query method.
        // The ISaaSPaymentRepository.GetByReferenceIdAsync is for business reference IDs.
        // Real implementation would add GetByStripePaymentIntentIdAsync to the repo.
        await Task.CompletedTask;
        return null;
    }
}
