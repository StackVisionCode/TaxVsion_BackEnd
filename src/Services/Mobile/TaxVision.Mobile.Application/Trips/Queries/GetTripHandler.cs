using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class GetTripHandler
{
    public static async Task<Result<TripResponse>> Handle(GetTripQuery q, ITripRepository trips, CancellationToken ct)
    {
        var trip = await trips.FindAsync(q.TenantId, q.CustomerId, q.TripId, ct);
        return trip is null || trip.DeletedAtUtc.HasValue
            ? Result.Failure<TripResponse>(TripErrors.NotFound)
            : Result.Success(TripResponse.From(trip));
    }
}
