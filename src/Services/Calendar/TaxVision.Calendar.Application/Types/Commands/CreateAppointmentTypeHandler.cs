using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Types.Commands;

public sealed record CreateAppointmentTypeCommand(
    Guid TenantId,
    string? Name,
    TimeSpan DefaultDuration,
    string? ColorHex,
    bool IsVirtual,
    bool BlocksOnConflict,
    int? DailyCap
);

public static class CreateAppointmentTypeHandler
{
    public static async Task<Result<AppointmentTypeResponse>> Handle(
        CreateAppointmentTypeCommand command,
        IAppointmentTypeRepository types,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var type = AppointmentType.Create(
            command.TenantId,
            command.Name,
            command.DefaultDuration,
            command.ColorHex,
            DateTime.UtcNow,
            command.IsVirtual,
            command.BlocksOnConflict,
            command.DailyCap
        );

        if (type.IsFailure)
            return Result.Failure<AppointmentTypeResponse>(type.Error);

        types.Add(type.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(AppointmentTypeResponse.From(type.Value));
    }
}
