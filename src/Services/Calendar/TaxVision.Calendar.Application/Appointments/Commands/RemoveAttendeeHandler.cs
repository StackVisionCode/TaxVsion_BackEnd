using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record RemoveAttendeeCommand(Guid TenantId, Guid AppointmentId, Guid AttendeeId, Guid ActingUserId);

public static class RemoveAttendeeHandler
{
    public static async Task<Result> Handle(
        RemoveAttendeeCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return found;

        var removed = found.Value.RemoveAttendee(command.AttendeeId, command.ActingUserId);
        if (removed.IsFailure)
            return removed;

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
