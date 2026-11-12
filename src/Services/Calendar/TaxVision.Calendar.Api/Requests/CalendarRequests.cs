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
    int? ReminderLeadMinutes = null,
    IReadOnlyList<AddAttendeeRequest>? Attendees = null
);

/// <param name="Scope">Sobre una serie es obligatorio; sin el se responde 400.</param>
/// <param name="ReminderLeadMinutes">Null deja el aviso como estaba; 0 lo quita.</param>
/// <summary>Aprobar una solicitud del portal; <c>AssignToUserId</c> se la da a otro empleado.</summary>
public sealed record ApproveBookingRequest(Guid? AssignToUserId);

public sealed record DeclineBookingRequest(string? Reason);

/// <summary>"Edit details": título, descripción y lugar (vacío = sin lugar).</summary>
public sealed record EditAppointmentDetailsRequest(string? Title, string? Description, string? Location);

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

/// <summary>El token del enlace del correo va en el cuerpo: en la URL acabaría en logs e historial.</summary>
public sealed record RsvpViewRequest(string Token);

public sealed record RsvpAnswerRequest(string Token, AttendeeResponse Response);

/// <summary>Portal: la respuesta del cliente a su invitación.</summary>
public sealed record PortalRespondRequest(AttendeeResponse Response);

/// <summary>Portal: reservar. El cliente sale del token; la clave de idempotencia, de la cabecera.</summary>
/// <summary><c>PreparerUserId</c> null = "Any available" (solo si la oficina aprueba cada reserva).</summary>
public sealed record PortalBookRequest(Guid TypeId, Guid? PreparerUserId, DateTime StartUtc);

public sealed record PortalCancelRequest(string? Reason);

public sealed record PortalRescheduleRequest(DateTime StartUtc);
