using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.Queries;

public static class SeatMapper
{
    public static SeatResponse ToResponse(SubscriptionSeat seat)
    {
        var currentAssignment = seat.CurrentAssignmentId is null
            ? null
            : seat.Assignments.FirstOrDefault(assignment => assignment.Id == seat.CurrentAssignmentId);

        return new SeatResponse(
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
            seat.BillingCycle.ToString(),
            seat.CurrentUserId,
            currentAssignment?.AssignedAtUtc
        );
    }
}
