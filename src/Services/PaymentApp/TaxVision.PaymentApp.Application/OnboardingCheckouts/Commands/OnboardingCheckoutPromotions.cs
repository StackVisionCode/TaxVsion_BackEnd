using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;

/// <summary>
/// Los códigos del alta en el pago. Si Auth todavía manda solo el formato viejo (una reserva y el descuento total),
/// se toma como una sola promoción.
/// </summary>
internal static class OnboardingCheckoutPromotions
{
    public static Result<SaaSPayment> Attach(
        Result<SaaSPayment> payment,
        CreateOnboardingCheckoutCommand command,
        long grossCents
    )
    {
        if (payment.IsFailure)
            return payment;

        var promotions = Build(command);
        if (promotions.IsFailure)
            return Result.Failure<SaaSPayment>(promotions.Error);
        if (promotions.Value.Count == 0)
            return payment;

        var attached = payment.Value.AttachPromotions(promotions.Value, grossCents);
        return attached.IsFailure ? Result.Failure<SaaSPayment>(attached.Error) : payment;
    }

    public static Result Renew(SaaSPayment existing, CreateOnboardingCheckoutCommand command)
    {
        var promotions = Build(command);
        if (promotions.IsFailure)
            return Result.Failure(promotions.Error);

        return promotions.Value.Count == 0 ? Result.Success() : existing.RenewPromotions(promotions.Value);
    }

    private static Result<IReadOnlyList<SaaSPaymentPromotion>> Build(CreateOnboardingCheckoutCommand command)
    {
        var requested =
            command.Promotions is { Count: > 0 } list ? list
            : command is { CodeReservationId: { } reservationId, DiscountAmountCents: > 0 }
                ?
                [
                    new OnboardingCheckoutPromotion(
                        reservationId,
                        command.DiscountAmountCents.Value,
                        command.PromotionSnapshotHash,
                        0
                    ),
                ]
            : [];

        var created = new List<SaaSPaymentPromotion>();
        foreach (var promotion in requested)
        {
            var result = SaaSPaymentPromotion.Create(
                promotion.CodeReservationId,
                promotion.DiscountAmountCents,
                promotion.PromotionSnapshotHash,
                promotion.Order
            );
            if (result.IsFailure)
                return Result.Failure<IReadOnlyList<SaaSPaymentPromotion>>(result.Error);
            created.Add(result.Value);
        }

        return Result.Success<IReadOnlyList<SaaSPaymentPromotion>>(created);
    }
}
