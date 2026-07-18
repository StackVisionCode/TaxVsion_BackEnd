using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Internal.Queries;

public static class GetUserAccessHandler
{
    public static async Task<Result<UserAccessResponse>> Handle(
        GetUserAccessQuery query,
        ISubscriptionRepository subscriptions,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<UserAccessResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var seat = await seats.GetByCurrentUserIdAsync(query.TenantId, query.UserId, ct);

        return Result.Success(
            new UserAccessResponse(
                query.UserId,
                query.TenantId,
                HasActiveSeat: seat?.Status == SeatStatus.Active,
                SeatId: seat?.Id,
                SeatStatus: seat?.Status.ToString(),
                SeatType: seat?.Type.ToString(),
                SeatExpiresAtUtc: seat?.CurrentPeriodEndUtc,
                SubscriptionStatus: subscription.Status.ToString(),
                PlanCode: subscription.PlanCode
            )
        );
    }
}
