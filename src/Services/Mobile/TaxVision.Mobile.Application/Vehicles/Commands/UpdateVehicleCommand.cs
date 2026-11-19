using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles.Commands;

public sealed record UpdateVehicleCommand(Guid TenantId, Guid CustomerId, Guid Id, VehicleInput Input);

public static class UpdateVehicleHandler
{
    public static async Task<Result<VehicleResponse>> Handle(
        UpdateVehicleCommand command,
        IVehicleRepository vehicles,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var vehicle = await vehicles.FindAsync(command.TenantId, command.CustomerId, command.Id, ct);
        if (vehicle is null)
            return Result.Failure<VehicleResponse>(VehicleErrors.NotFound);
        var details = command.Input.Validate();
        if (details.IsFailure)
            return Result.Failure<VehicleResponse>(details.Error);
        var now = clock.GetUtcNow().UtcDateTime;
        if (command.Input.IsDefault)
            await vehicles.ClearDefaultAsync(command.TenantId, command.CustomerId, now, ct);
        vehicle.Update(details.Value, command.Input.IsDefault, now);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(VehicleResponse.From(vehicle));
    }
}
