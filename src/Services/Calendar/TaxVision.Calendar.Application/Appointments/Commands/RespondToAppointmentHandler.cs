using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record RespondToAppointmentCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid RespondingUserId,
    AttendeeResponse Response
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
