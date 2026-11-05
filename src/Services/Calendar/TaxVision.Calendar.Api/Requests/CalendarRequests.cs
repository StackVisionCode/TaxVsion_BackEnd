using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Api.Requests;

/// <param name="ReminderLeadMinutes">Minutos del aviso antes de la cita; null o 0 = sin aviso.</param>
public sealed record ScheduleAppointmentRequest(
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
);

/// <param name="Scope">Sobre una serie es obligatorio; sin el se responde 400.</param>
/// <param name="ReminderLeadMinutes">Null deja el aviso como estaba; 0 lo quita.</param>
public sealed record RescheduleAppointmentRequest(
    EditScope? Scope,
    DateTime? OriginalStartUtc,
    DateTime? NewStartUtc,
    DateTime? NewEndUtc,
    DateOnly? SeriesStartDate,
    TimeOnly? LocalStartTime,
    TimeSpan? Duration,
    string? TimeZoneId,
    string? RecurrenceRule,
    int? ReminderLeadMinutes = null
);

public sealed record AddAttendeeRequest(
    AttendeeKind Kind,
    Guid? UserId,
    Guid? CustomerId,
    string? DisplayName,
    string? Email,
    bool IsRequired
);

public sealed record CancelAppointmentRequest(EditScope? Scope, DateTime? OriginalStartUtc, string? Reason);

public sealed record RespondToAppointmentRequest(AttendeeResponse Response);

public sealed record CreateAppointmentTypeRequest(
    string? Name,
    TimeSpan DefaultDuration,
    string? ColorHex,
    bool IsVirtual,
    bool BlocksOnConflict,
    int? DailyCap
);
