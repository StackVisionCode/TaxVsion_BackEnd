using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

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
    IReadOnlyList<AppointmentAttendeeResponse> Attendees
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
            attendees
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
    string? Location
)
{
    public static OccurrenceResponse From(Occurrence occurrence) =>
        new(
            occurrence.AppointmentId,
            occurrence.OriginalStartUtc,
            occurrence.StartUtc,
            occurrence.EndUtc,
            occurrence.IsException,
            occurrence.Title,
            occurrence.Location
        );
}

/// <summary>
/// Lo que devuelve un POST o un reagendado: la cita y, si hubo solapamiento que no bloquea, el aviso.
/// </summary>
public sealed record AppointmentWithWarnings(AppointmentResponse Appointment, IReadOnlyList<string> Warnings);
