using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles.Commands;

public sealed record DeleteVehicleCommand(Guid TenantId, Guid CustomerId, Guid Id);

public static class DeleteVehicleHandler
{
    public static async Task<Result> Handle(
        DeleteVehicleCommand command,
        IVehicleRepository vehicles,
        IUnitOfWork unitOfWork,
        ITripRepository trips,
        CancellationToken ct
    )
    {
        var vehicle = await vehicles.FindAsync(command.TenantId, command.CustomerId, command.Id, ct);
        if (vehicle is null)
            return Result.Failure(VehicleErrors.NotFound);
        if (await trips.HasVehicleTripsAsync(command.TenantId, command.CustomerId, command.Id, ct))
            return Result.Failure(TripErrors.VehicleInUse);
        vehicles.Remove(vehicle);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
