using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class ActiveTripsHandler
{
    public static async Task<IReadOnlyList<TripResponse>> Handle(
        ActiveTripsQuery q,
        ITripRepository trips,
        CancellationToken ct
    ) => (await trips.ActiveAsync(q.TenantId, q.CustomerId, ct)).Select(t => TripResponse.From(t)).ToArray();
}
