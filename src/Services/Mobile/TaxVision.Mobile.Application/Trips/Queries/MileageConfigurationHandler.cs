using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class MileageConfigurationHandler
{
    public static async Task<Result<MileageConfigurationResponse>> Handle(
        MileageConfigurationQuery q,
        ITripRepository trips,
        CancellationToken ct
    )
    {
        if (!TripPurpose.IsValid(q.Purpose) || q.Date < new DateOnly(1970, 1, 1))
            return Result.Failure<MileageConfigurationResponse>(TripErrors.Invalid);
        var rate = await trips.EffectiveRateAsync(q.TenantId, q.Purpose, q.Date, ct);
        return Result.Success(
            new MileageConfigurationResponse(
                q.Date,
                q.Purpose,
                "miles",
                rate?.Id,
                rate?.RatePerMile,
                rate?.Currency,
                rate is not null
            )
        );
    }
}
