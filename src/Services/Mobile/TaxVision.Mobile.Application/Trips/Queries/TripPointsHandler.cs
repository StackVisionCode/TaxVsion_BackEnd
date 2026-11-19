using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class TripPointsHandler
{
    public static async Task<Result<TripPointsPage>> Handle(
        TripPointsQuery q,
        ITripRepository trips,
        CancellationToken ct
    )
    {
        if (q.After < -1 || q.Size is < 1 or > 512)
            return Result.Failure<TripPointsPage>(TripErrors.Invalid);
        var trip = await trips.FindAsync(q.TenantId, q.CustomerId, q.TripId, ct);
        if (trip is null || trip.DeletedAtUtc.HasValue)
            return Result.Failure<TripPointsPage>(TripErrors.NotFound);
        return Result.Success(await trips.PointsAsync(q.TenantId, q.CustomerId, q.TripId, q.After, q.Size, ct));
    }
}
