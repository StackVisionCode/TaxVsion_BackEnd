using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class ListMileageRatesHandler
{
    public static async Task<Result<PagedResult<MileageRateResponse>>> Handle(
        ListMileageRatesQuery q,
        ITripRepository trips,
        CancellationToken ct
    ) =>
        TripValidation.ValidPage(q.Page, q.Size)
            ? Result.Success(await trips.RatesAsync(q.TenantId, q.Page, q.Size, ct))
            : Result.Failure<PagedResult<MileageRateResponse>>(TripErrors.Invalid);
}
