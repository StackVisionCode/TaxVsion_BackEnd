using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles.Queries;

public sealed record ListVehiclesQuery(Guid TenantId, Guid CustomerId, int Page = 1, int Size = 50);

public static class ListVehiclesHandler
{
    public static async Task<Result<PagedResult<VehicleResponse>>> Handle(
        ListVehiclesQuery query,
        IVehicleRepository vehicles,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100 || (long)(query.Page - 1) * query.Size > int.MaxValue)
            return Result.Failure<PagedResult<VehicleResponse>>(VehicleErrors.InvalidPage);
        var page = await vehicles.ListAsync(query.TenantId, query.CustomerId, query.Page, query.Size, ct);
        return Result.Success(
            new PagedResult<VehicleResponse>(
                page.Items.Select(VehicleResponse.From).ToArray(),
                page.Page,
                page.Size,
                page.TotalCount
            )
        );
    }
}
