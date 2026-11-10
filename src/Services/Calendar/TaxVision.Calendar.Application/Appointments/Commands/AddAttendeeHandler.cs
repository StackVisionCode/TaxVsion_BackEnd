using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record AddAttendeeCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid ActingUserId,
    AttendeeKind Kind,
    Guid? UserId,
    Guid? CustomerId,
    string? DisplayName,
    string? Email,
    bool IsRequired,
    bool CanManageAll
);

/// <summary>Suma un asistente. Si es del equipo, revisa su agenda: avisa del choque o lo bloquea si el tipo lo pide.</summary>
public static class AddAttendeeHandler
{
    public static async Task<Result<AppointmentWithWarnings>> Handle(
        AddAttendeeCommand command,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return Result.Failure<AppointmentWithWarnings>(found.Error);

        var snapshot = AttendeeSnapshot.Create(command.DisplayName, command.Email);
        if (snapshot.IsFailure)
            return Result.Failure<AppointmentWithWarnings>(snapshot.Error);

        var appointment = found.Value;
        var added = appointment.AddAttendee(
            command.Kind,
            command.UserId,
            command.CustomerId,
            snapshot.Value,
            command.IsRequired,
            command.ActingUserId,
            DateTime.UtcNow,
            command.CanManageAll
        );

        if (added.IsFailure)
            return Result.Failure<AppointmentWithWarnings>(added.Error);

        IReadOnlyList<string> warnings = [];
        if (command.UserId is { } userId && appointment.Status != AppointmentStatus.Cancelled)
        {
            var type = await types.GetByIdAsync(command.TenantId, appointment.AppointmentTypeId, ct);
            if (type.IsSuccess)
            {
                var conflict = await ConflictProbe.CheckAttendeeAsync(
                    appointment,
                    userId,
                    type.Value,
                    appointments,
                    availability,
                    ct
                );
                if (conflict.IsFailure)
                    return Result.Failure<AppointmentWithWarnings>(conflict.Error);
                warnings = conflict.Value;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AppointmentWithWarnings(AppointmentResponse.From(appointment), warnings));
    }
}
