using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class TripStatisticsHandler
{
    public static async Task<Result<TripStatisticsResponse>> Handle(
        TripStatisticsQuery q,
        ITripRepository trips,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (!IanaTimeZone.TryFindTimeZone(q.TimeZone, out var zone))
            return Result.Failure<TripStatisticsResponse>(TripErrors.Invalid);
        return Result.Success(
            await trips.StatisticsAsync(q.TenantId, q.CustomerId, clock.GetUtcNow().UtcDateTime, zone, q.TimeZone, ct)
        );
    }
}
