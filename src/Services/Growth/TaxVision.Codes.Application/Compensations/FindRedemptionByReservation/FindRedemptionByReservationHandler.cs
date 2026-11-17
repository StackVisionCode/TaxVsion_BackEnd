using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;

namespace TaxVision.Codes.Application.Compensations.FindRedemptionByReservation;

/// <summary>El canje que nació de una reserva (null si la reserva no se llegó a confirmar), para compensarlo.</summary>
public sealed record FindRedemptionByReservationQuery(Guid TenantId, Guid ReservationId);

public sealed record RedemptionToCompensate(Guid RedemptionId, long DiscountAmountCents, string Currency);

public static class FindRedemptionByReservationHandler
{
    public static async Task<Result<RedemptionToCompensate?>> Handle(
        FindRedemptionByReservationQuery query,
        ICodeRedemptionRepository redemptions,
        CancellationToken ct
    )
    {
        if (query.TenantId == Guid.Empty || query.ReservationId == Guid.Empty)
            return Result.Failure<RedemptionToCompensate?>(
                new Error("Codes.FindRedemption.InvalidRequest", "TenantId and ReservationId are required.")
            );

        var redemption = await redemptions.GetByReservationIdAsync(query.TenantId, query.ReservationId, ct);
        return Result.Success<RedemptionToCompensate?>(
            redemption is null
                ? null
                : new RedemptionToCompensate(
                    redemption.Id,
                    redemption.DiscountAmount.AmountCents,
                    redemption.DiscountAmount.Currency
                )
        );
    }
}
