using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Payments;

namespace TaxVision.Billing.Application.Payments.ResolveUnappliedPayment;

/// <summary>La oficina cierra un cobro no aplicado con una nota. El reembolso lo hace una persona en PaymentClient.</summary>
public sealed record ResolveUnappliedPaymentCommand(
    Guid TenantId,
    Guid UnappliedPaymentId,
    string Resolution,
    string? Note,
    Guid ActorUserId
);

public static class ResolveUnappliedPaymentHandler
{
    public static async Task<Result> Handle(
        ResolveUnappliedPaymentCommand command,
        IUnappliedPaymentRepository unappliedPayments,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<UnappliedPaymentResolution>(command.Resolution, ignoreCase: true, out var resolution)
            || !Enum.IsDefined(resolution))
            return Result.Failure(
                new Error("Billing.UnappliedPayment.InvalidResolution", "Resolution must be Refunded or AppliedAsCredit.")
            );

        var payment = await unappliedPayments.GetByIdAsync(command.TenantId, command.UnappliedPaymentId, ct);
        if (payment is null)
            return Result.Failure(new Error("Billing.UnappliedPayment.NotFound", "Unapplied payment does not exist."));

        var resolved = payment.Resolve(resolution, command.Note, command.ActorUserId, clock.GetUtcNow().UtcDateTime);
        if (resolved.IsFailure)
            return resolved;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
