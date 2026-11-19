using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class ListTripsHandler
{
    public static async Task<Result<PagedResult<TripResponse>>> Handle(
        ListTripsQuery q,
        ITripRepository trips,
        CancellationToken ct
    )
    {
        if (!TripValidation.ValidFilter(q.Filter))
            return Result.Failure<PagedResult<TripResponse>>(TripErrors.Invalid);
        var page = await trips.ListAsync(q.TenantId, q.CustomerId, q.Filter, ct);
        return Result.Success(
            new PagedResult<TripResponse>(
                page.Items.Select(t => TripResponse.From(t)).ToArray(),
                page.Page,
                page.Size,
                page.TotalCount
            )
        );
    }
}
