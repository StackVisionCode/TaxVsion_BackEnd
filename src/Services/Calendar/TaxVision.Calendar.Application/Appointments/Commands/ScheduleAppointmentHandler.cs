using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.Appointments.Commands;

public sealed record ScheduleAppointmentCommand(
    Guid TenantId,
    Guid OrganizerUserId,
    string? Title,
    string? Description,
    string? Location,
    Guid AppointmentTypeId,
    string? TimeZoneId,
    DateTime? StartUtc,
    DateTime? EndUtc,
    DateOnly? SeriesStartDate,
    TimeOnly? LocalStartTime,
    TimeSpan? Duration,
    string? RecurrenceRule,
    Guid? CustomerId,
    int? TaxYear,
    bool IsVirtual,
    int? ReminderLeadMinutes = null
)
{
    /// <summary>Nombre y correo de quien agenda (del token); sin correo no se suma como asistente.</summary>
    public OrganizerContact? Organizer { get; init; }

    /// <summary>Quienes vienen con el alta: personal, cliente o externos.</summary>
    public IReadOnlyList<ScheduleAttendeeInput> Attendees { get; init; } = [];

    /// <summary>Nombre del cliente cuando la reserva viene del portal; null si la agenda el equipo.</summary>
    public string? BookedByCustomerName { get; init; }

    /// <summary>La pide un cliente y la oficina debe aprobarla: queda pedida, aparta el horario y no se anuncia.</summary>
    public bool AwaitingApproval { get; init; }
}

public sealed record OrganizerContact(string? DisplayName, string? Email);

/// <param name="Accepted">Ya dijo que va (el cliente que reserva su propia cita).</param>
public sealed record ScheduleAttendeeInput(
    AttendeeKind Kind,
    Guid? UserId,
    Guid? CustomerId,
    string? DisplayName,
    string? Email,
    bool IsRequired,
    bool Accepted = false
);

public static class ScheduleAppointmentHandler
{
    public static async Task<Result<AppointmentWithWarnings>> Handle(
        ScheduleAppointmentCommand command,
        IAppointmentRepository appointments,
        IAppointmentTypeRepository types,
        IAvailabilityRepository availability,
        IOrganizerScheduleLock scheduleLock,
        IUnitOfWork unitOfWork,
        ICalendarMetrics metrics,
        CancellationToken ct
    )
    {
        var built = await AppointmentFactory.BuildAsync(command, types, ct);
        if (built.IsFailure)
            return Result.Failure<AppointmentWithWarnings>(built.Error);

        var (appointment, type) = built.Value;

        // Comprobar e insertar en serie por organizador: el choque con un tipo bloqueante es regla dura (D-CAL-2).
        await scheduleLock.AcquireAsync(command.TenantId, appointment.OrganizerUserId, ct);

        var conflict = await ConflictProbe.CheckAsync(appointment, type, appointments, availability, ct);
        if (conflict.IsFailure)
        {
            metrics.RecordConflictDetected(blocked: true);
            return Result.Failure<AppointmentWithWarnings>(conflict.Error);
        }

        if (conflict.Value.Count > 0)
            metrics.RecordConflictDetected(blocked: false);

        appointments.Add(appointment);
        await unitOfWork.SaveChangesAsync(ct);

        metrics.RecordCreated(appointment.IsRecurring);

        return Result.Success(new AppointmentWithWarnings(AppointmentResponse.From(appointment), conflict.Value));
    }
}
