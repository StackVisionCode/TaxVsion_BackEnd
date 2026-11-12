using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Types.Abstractions;

namespace TaxVision.Calendar.Application.Types.Commands;

public sealed record UpdateAppointmentTypeCommand(
    Guid TenantId,
    Guid TypeId,
    string? Name,
    TimeSpan DefaultDuration,
    string? ColorHex,
    bool IsVirtual,
    bool BlocksOnConflict,
    int? DailyCap
);

/// <summary>Edita un tipo. Las citas ya agendadas conservan su hora; solo cambia lo que viene.</summary>
public static class UpdateAppointmentTypeHandler
{
    public static async Task<Result<AppointmentTypeResponse>> Handle(
        UpdateAppointmentTypeCommand command,
        IAppointmentTypeRepository types,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await types.GetByIdAsync(command.TenantId, command.TypeId, ct);
        if (found.IsFailure)
            return Result.Failure<AppointmentTypeResponse>(found.Error);

        var updated = found.Value.Update(
            command.Name,
            command.DefaultDuration,
            command.ColorHex,
            command.IsVirtual,
            command.BlocksOnConflict,
            command.DailyCap
        );
        if (updated.IsFailure)
            return Result.Failure<AppointmentTypeResponse>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(AppointmentTypeResponse.From(found.Value));
    }
}
