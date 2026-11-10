using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Appointments;

public sealed record AppointmentAttendeeResponse(
    Guid Id,
    string Kind,
    Guid? UserId,
    Guid? CustomerId,
    string DisplayName,
    string? Email,
    bool IsRequired,
    string Response,
    DateTime? RespondedAtUtc
);

public sealed record AppointmentResponse(
    Guid Id,
    string Title,
    string? Description,
    string? Location,
    string Status,
    Guid AppointmentTypeId,
    Guid OrganizerUserId,
    string TimeZoneId,
    DateTime? StartUtc,
    DateTime? EndUtc,
    string? RecurrenceRule,
    Guid? SplitFromSeriesId,
    Guid? CustomerId,
    int? TaxYear,
    bool IsVirtual,
    string? MeetingShortCode,
    IReadOnlyList<AppointmentAttendeeResponse> Attendees,
    int? ReminderLeadMinutes
)
{
    public static AppointmentResponse From(Appointment appointment)
    {
        var attendees = new List<AppointmentAttendeeResponse>();
        foreach (var attendee in appointment.Attendees)
        {
            attendees.Add(
                new AppointmentAttendeeResponse(
                    attendee.Id,
                    attendee.Kind.ToString(),
                    attendee.UserId,
                    attendee.CustomerId,
                    attendee.Snapshot.DisplayName,
                    attendee.Snapshot.Email,
                    attendee.IsRequired,
                    attendee.Response.ToString(),
                    attendee.RespondedAtUtc
                )
            );
        }

        return new AppointmentResponse(
            appointment.Id,
            appointment.Title.Value,
            appointment.Description,
            appointment.Location?.Value,
            appointment.Status.ToString(),
            appointment.AppointmentTypeId,
            appointment.OrganizerUserId,
            appointment.Timing.TimeZone.Id,
            appointment.Timing.StartUtc,
            appointment.Timing.EndUtc,
            appointment.Recurrence?.Value,
            appointment.SplitFromSeriesId,
            appointment.CustomerId,
            appointment.TaxYear,
            appointment.IsVirtual,
            appointment.MeetingShortCode,
            attendees,
            appointment.ReminderLeadMinutes
        );
    }
}

/// <summary>Una ocurrencia en un listado de rango. Es lo que pinta el calendario.</summary>
public sealed record OccurrenceResponse(
    Guid AppointmentId,
    DateTime OriginalStartUtc,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsException,
    string Title,
    string? Location,
    // Lo que el calendario necesita para pintar y enlazar sin pedir el detalle de cada cita.
    Guid AppointmentTypeId,
    string? TypeName,
    string? ColorHex,
    string Status,
    Guid OrganizerUserId,
    Guid? CustomerId,
    bool IsVirtual,
    Guid? MeetingId,
    string? MeetingShortCode,
    bool IsRecurring
)
{
    public static OccurrenceResponse From(Occurrence occurrence, Appointment appointment, AppointmentType? type) =>
        new(
            occurrence.AppointmentId,
            occurrence.OriginalStartUtc,
            occurrence.StartUtc,
            occurrence.EndUtc,
            occurrence.IsException,
            occurrence.Title,
            occurrence.Location,
            appointment.AppointmentTypeId,
            type?.Name,
            type?.ColorHex,
            appointment.Status.ToString(),
            appointment.OrganizerUserId,
            appointment.CustomerId,
            appointment.IsVirtual,
            appointment.MeetingId,
            appointment.MeetingShortCode,
            appointment.IsRecurring
        );
}

/// <summary>
/// Lo que devuelve un POST o un reagendado: la cita y, si hubo solapamiento que no bloquea, el aviso.
/// </summary>
public sealed record AppointmentWithWarnings(AppointmentResponse Appointment, IReadOnlyList<string> Warnings);
