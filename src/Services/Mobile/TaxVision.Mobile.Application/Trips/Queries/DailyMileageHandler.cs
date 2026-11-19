using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class DailyMileageHandler
{
    public static async Task<Result<IReadOnlyList<DailyMileageResponse>>> Handle(
        DailyMileageQuery q,
        ITripRepository trips,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (q.Days is < 1 or > 366 || !IanaTimeZone.TryFindTimeZone(q.TimeZone, out var zone))
            return Result.Failure<IReadOnlyList<DailyMileageResponse>>(TripErrors.Invalid);
        var end =
            q.EndDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, zone));
        if (end < new DateOnly(1971, 1, 1) || end > new DateOnly(9998, 12, 31))
            return Result.Failure<IReadOnlyList<DailyMileageResponse>>(TripErrors.Invalid);
        return Result.Success(await trips.DailyAsync(q.TenantId, q.CustomerId, end.AddDays(1 - q.Days), end, zone, ct));
    }
}
