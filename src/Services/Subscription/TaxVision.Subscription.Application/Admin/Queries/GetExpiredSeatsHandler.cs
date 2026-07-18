using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Admin.Queries;

public static class GetExpiredSeatsHandler
{
    public static async Task<Result<PagedResult<AdminSeatResponse>>> Handle(
        GetExpiredSeatsQuery query,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var (items, totalCount) = await seats.GetExpiredAsync(page, pageSize, ct);

        var response = new List<AdminSeatResponse>(items.Count);
        foreach (var seat in items)
            response.Add(new AdminSeatResponse(seat.TenantId, seat.Id, seat.Status.ToString(), seat.ExpiredAtUtc));

        return Result.Success(new PagedResult<AdminSeatResponse>(response, page, pageSize, totalCount));
    }
}
