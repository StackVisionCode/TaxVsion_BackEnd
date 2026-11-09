using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
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

public static class AddAttendeeHandler
{
    public static async Task<Result<AppointmentResponse>> Handle(
        AddAttendeeCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return Result.Failure<AppointmentResponse>(found.Error);

        var snapshot = AttendeeSnapshot.Create(command.DisplayName, command.Email);
        if (snapshot.IsFailure)
            return Result.Failure<AppointmentResponse>(snapshot.Error);

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
            return Result.Failure<AppointmentResponse>(added.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(AppointmentResponse.From(appointment));
    }
}
