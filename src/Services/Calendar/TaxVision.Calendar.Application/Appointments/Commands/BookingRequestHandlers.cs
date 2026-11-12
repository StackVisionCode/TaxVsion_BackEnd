using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary><paramref name="AssignToUserId"/>: otro empleado que pasa a organizarla; null = quien aprueba se la queda.</summary>
public sealed record ApproveBookingRequestCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid ActingUserId,
    Guid? AssignToUserId,
    bool CanManageAll
);

/// <summary>
/// Aprueba la solicitud de un cliente. Al asignarla a otro empleado se revisa su agenda: un tipo que no
/// admite solapes rechaza la aprobación, sin dejar nada a medias.
/// </summary>
public static class ApproveBookingRequestHandler
{
    public static async Task<Result<AppointmentWithWarnings>> Handle(
        ApproveBookingRequestCommand command,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IStaffContactClient contacts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return Result.Failure<AppointmentWithWarnings>(found.Error);

        var appointment = found.Value;
        AppointmentAssignee? assignee = null;
        if (command.AssignToUserId is { } userId && userId != appointment.OrganizerUserId)
        {
            var contact = await contacts.FindAsync(command.TenantId, userId, ct);
            if (contact is not { IsActive: true })
                return Result.Failure<AppointmentWithWarnings>(AppointmentErrors.AssigneeUnavailable);
            var snapshot = AttendeeSnapshot.Create(contact.DisplayName ?? contact.Email, contact.Email);
            if (snapshot.IsFailure)
                return Result.Failure<AppointmentWithWarnings>(snapshot.Error);
            assignee = new AppointmentAssignee(userId, snapshot.Value);
        }

        var approved = appointment.ApproveRequest(
            command.ActingUserId,
            command.CanManageAll,
            assignee,
            DateTime.UtcNow
        );
        if (approved.IsFailure)
            return Result.Failure<AppointmentWithWarnings>(approved.Error);

        IReadOnlyList<string> warnings = [];
        var type = await types.GetByIdAsync(command.TenantId, appointment.AppointmentTypeId, ct);
        if (type.IsSuccess)
        {
            var conflict = await ConflictProbe.CheckAsync(appointment, type.Value, appointments, availability, ct);
            if (conflict.IsFailure)
            {
                appointments.DiscardPendingChanges();
                return Result.Failure<AppointmentWithWarnings>(conflict.Error);
            }
            warnings = conflict.Value;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new AppointmentWithWarnings(AppointmentResponse.From(appointment), warnings));
    }
}

public sealed record DeclineBookingRequestCommand(
    Guid TenantId,
    Guid AppointmentId,
    Guid ActingUserId,
    string? Reason,
    bool CanManageAll
);

/// <summary>Rechaza la solicitud: se libera el horario y el cliente recibe el motivo por correo.</summary>
public static class DeclineBookingRequestHandler
{
    public static async Task<Result> Handle(
        DeclineBookingRequestCommand command,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(command.TenantId, command.AppointmentId, ct);
        if (found.IsFailure)
            return found;

        var declined = found.Value.DeclineRequest(
            command.ActingUserId,
            command.CanManageAll,
            command.Reason,
            DateTime.UtcNow
        );
        if (declined.IsFailure)
            return declined;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
