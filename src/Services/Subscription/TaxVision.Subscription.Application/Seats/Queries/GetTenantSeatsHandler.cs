using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.Queries;

public static class GetTenantSeatsHandler
{
    public static async Task<Result<PagedResult<SeatResponse>>> Handle(
        GetTenantSeatsQuery query,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        var allSeats = await seats.GetByTenantIdAsync(query.TenantId, ct);
        var filtered = ApplyFilters(allSeats, query);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(SeatMapper.ToResponse).ToList();

        return Result.Success(new PagedResult<SeatResponse>(pageItems, page, pageSize, filtered.Count));
    }

    private static List<SubscriptionSeat> ApplyFilters(IReadOnlyList<SubscriptionSeat> seats, GetTenantSeatsQuery query)
    {
        var result = new List<SubscriptionSeat>(seats.Count);
        foreach (var seat in seats)
        {
            if (
                query.Status is { Length: > 0 }
                && !string.Equals(seat.Status.ToString(), query.Status, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            if (
                query.Type is { Length: > 0 }
                && !string.Equals(seat.Type.ToString(), query.Type, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            if (query.UserId is { } userId && seat.CurrentUserId != userId)
                continue;

            result.Add(seat);
        }

        return result;
    }
}
