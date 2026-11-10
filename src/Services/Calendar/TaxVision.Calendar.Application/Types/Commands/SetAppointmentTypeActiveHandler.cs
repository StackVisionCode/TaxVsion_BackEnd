using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Types.Abstractions;

namespace TaxVision.Calendar.Application.Types.Commands;

public sealed record SetAppointmentTypeActiveCommand(Guid TenantId, Guid TypeId, bool IsActive);

/// <summary>Desactiva o reactiva un tipo. No se borra: las citas pasadas lo siguen nombrando.</summary>
public static class SetAppointmentTypeActiveHandler
{
    public static async Task<Result<AppointmentTypeResponse>> Handle(
        SetAppointmentTypeActiveCommand command,
        IAppointmentTypeRepository types,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await types.GetByIdAsync(command.TenantId, command.TypeId, ct);
        if (found.IsFailure)
            return Result.Failure<AppointmentTypeResponse>(found.Error);

        if (command.IsActive)
            found.Value.Reactivate();
        else
            found.Value.Deactivate();

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(AppointmentTypeResponse.From(found.Value));
    }
}
