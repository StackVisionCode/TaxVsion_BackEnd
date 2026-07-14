using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Admin.Queries;

public static class GetUpcomingRenewalsHandler
{
    private const int BatchSize = 500;

    public static async Task<Result<IReadOnlyList<UpcomingRenewalResponse>>> Handle(
        GetUpcomingRenewalsQuery query, ISubscriptionRepository subscriptions, ISubscriptionSeatRepository seats, CancellationToken ct)
    {
        var daysAhead = query.DaysAhead is < 1 or > 90 ? 7 : query.DaysAhead;
        var nowUtc = DateTime.UtcNow;
        var windowEndUtc = nowUtc.AddDays(daysAhead);

        var dueSubscriptions = await subscriptions.GetRenewingBetweenAsync(nowUtc, windowEndUtc, BatchSize, ct);
        var dueSeats = await seats.GetRenewingBetweenAsync(nowUtc, windowEndUtc, BatchSize, ct);

        var response = new List<UpcomingRenewalResponse>(dueSubscriptions.Count + dueSeats.Count);
        foreach (var subscription in dueSubscriptions)
            response.Add(new UpcomingRenewalResponse(subscription.TenantId, "TenantSubscription", subscription.Id, subscription.NextRenewalAtUtc!.Value));

        foreach (var seat in dueSeats)
            response.Add(new UpcomingRenewalResponse(seat.TenantId, "SubscriptionSeat", seat.Id, seat.NextRenewalAtUtc!.Value));

        return Result.Success<IReadOnlyList<UpcomingRenewalResponse>>(response);
    }
}
