using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles.Queries;

public sealed record GetVehicleQuery(Guid TenantId, Guid CustomerId, Guid Id);

public static class GetVehicleHandler
{
    public static async Task<Result<VehicleResponse>> Handle(
        GetVehicleQuery query,
        IVehicleRepository vehicles,
        CancellationToken ct
    )
    {
        var vehicle = await vehicles.FindAsync(query.TenantId, query.CustomerId, query.Id, ct);
        return vehicle is null
            ? Result.Failure<VehicleResponse>(VehicleErrors.NotFound)
            : Result.Success(VehicleResponse.From(vehicle));
    }
}
