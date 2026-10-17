using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.Queries;

public static class SeatMapper
{
    public static SeatResponse ToResponse(SubscriptionSeat seat) =>
        new(
            seat.Id,
            seat.Type.ToString(),
            seat.Status.ToString(),
            seat.SourceType.ToString(),
            seat.SourceReferenceId,
            seat.PurchasedAtUtc,
            seat.CurrentPeriodStartUtc,
            seat.CurrentPeriodEndUtc,
            seat.NextRenewalAtUtc,
            seat.AutoRenew,
            seat.BillingCycle.ToString()
        );
}
