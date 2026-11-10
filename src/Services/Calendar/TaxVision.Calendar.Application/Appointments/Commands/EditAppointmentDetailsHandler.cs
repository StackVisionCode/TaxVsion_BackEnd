using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary>Location vacío = sin lugar.</summary>
public sealed record EditAppointmentDetailsCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid ActingUserId,
    string? Title,
    string? Description,
    string? Location,
    bool CanManageAll
);

/// <summary>"Edit details": título, descripción y lugar. La hora, los invitados y el tipo van por su lado.</summary>
public static class EditAppointmentDetailsHandler
{
    public static async Task<Result<AppointmentResponse>> Handle(
        EditAppointmentDetailsCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var title = AppointmentTitle.Create(command.Title);
        if (title.IsFailure)
            return Result.Failure<AppointmentResponse>(title.Error);

        Location? location = null;
        if (!string.IsNullOrWhiteSpace(command.Location))
        {
            var parsed = Location.Create(command.Location);
            if (parsed.IsFailure)
                return Result.Failure<AppointmentResponse>(parsed.Error);
            location = parsed.Value;
        }

        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return Result.Failure<AppointmentResponse>(found.Error);

        var appointment = found.Value;
        var edited = appointment.EditDetails(
            title.Value,
            command.Description,
            location,
            command.ActingUserId,
            command.CanManageAll,
            DateTime.UtcNow
        );
        if (edited.IsFailure)
            return Result.Failure<AppointmentResponse>(edited.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(AppointmentResponse.From(appointment));
    }
}
