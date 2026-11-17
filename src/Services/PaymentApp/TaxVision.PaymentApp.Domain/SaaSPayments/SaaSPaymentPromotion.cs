using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>Un código de descuento de Growth aplicado al pago (su reserva). Un reembolso o contracargo lo compensa.</summary>
public sealed class SaaSPaymentPromotion
{
    public const int SnapshotHashMaxLength = 128;

    private SaaSPaymentPromotion() { }

    public Guid Id { get; private set; }
    public Guid CodeReservationId { get; private set; }
    public long DiscountAmountCents { get; private set; }
    public string? PromotionSnapshotHash { get; private set; }
    public int Order { get; private set; }

    public static Result<SaaSPaymentPromotion> Create(
        Guid codeReservationId,
        long discountAmountCents,
        string? promotionSnapshotHash,
        int order
    )
    {
        if (codeReservationId == Guid.Empty || discountAmountCents <= 0 || order < 0)
            return Result.Failure<SaaSPaymentPromotion>(
                new Error("SaaSPaymentPromotion.Invalid", "A promotion needs its reservation and a positive discount.")
            );

        var hash = string.IsNullOrWhiteSpace(promotionSnapshotHash) ? null : promotionSnapshotHash.Trim();
        if (hash is { Length: > SnapshotHashMaxLength })
            return Result.Failure<SaaSPaymentPromotion>(
                new Error("SaaSPaymentPromotion.Invalid", "The promotion snapshot hash is too long.")
            );

        return Result.Success(
            new SaaSPaymentPromotion
            {
                Id = Guid.NewGuid(),
                CodeReservationId = codeReservationId,
                DiscountAmountCents = discountAmountCents,
                PromotionSnapshotHash = hash,
                Order = order,
            }
        );
    }
}

/// <summary>Una promoción del pago, tal como viaja en el snapshot y en los eventos.</summary>
public sealed record SaaSPaymentPromotionSnapshot(Guid CodeReservationId, long DiscountAmountCents, int Order);
