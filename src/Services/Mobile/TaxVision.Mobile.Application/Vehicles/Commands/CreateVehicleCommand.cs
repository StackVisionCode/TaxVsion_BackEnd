using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Vehicles.Abstractions;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Application.Vehicles.Commands;

public sealed record CreateVehicleCommand(Guid TenantId, Guid CustomerId, VehicleInput Input);

public static class CreateVehicleHandler
{
    public static async Task<Result<VehicleResponse>> Handle(
        CreateVehicleCommand command,
        IVehicleRepository vehicles,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var details = command.Input.Validate();
        if (details.IsFailure)
            return Result.Failure<VehicleResponse>(details.Error);
        var now = clock.GetUtcNow().UtcDateTime;
        var created = Vehicle.Create(command.TenantId, command.CustomerId, details.Value, command.Input.IsDefault, now);
        if (created.IsFailure)
            return Result.Failure<VehicleResponse>(created.Error);
        if (command.Input.IsDefault)
            await vehicles.ClearDefaultAsync(command.TenantId, command.CustomerId, now, ct);
        vehicles.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(VehicleResponse.From(created.Value));
    }
}
