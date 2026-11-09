using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <param name="ExpectedSchedule">Lo que el endpoint leyó antes del primer intento. Si cambió, no se responde.</param>
public sealed record RespondToAppointmentCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid RespondingUserId,
    AttendeeResponse Response,
    string? ExpectedSchedule = null
);

public static class RespondToAppointmentHandler
{
    public static async Task<Result> Handle(
        RespondToAppointmentCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return found;

        var appointment = found.Value;

        // El reintento relee: sin esto, aceptaría en silencio una hora que la persona no vio.
        if (command.ExpectedSchedule is { } expected && expected != appointment.ScheduleStamp)
            return Result.Failure(AppointmentErrors.ScheduleChanged);

        var responded = appointment.RespondAsAttendee(
            command.Response,
            command.RespondingUserId,
            null,
            null,
            DateTime.UtcNow
        );

        if (responded.IsFailure)
            return responded;

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
