using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Rsvp;

/// <summary>
/// Lo que ve el invitado al abrir su enlace: la cita y su respuesta. Ni correos ni otros invitados:
/// el enlace puede reenviarse y no debe filtrar nada más.
/// </summary>
public sealed record AppointmentRsvpView(
    string Title,
    DateTime? StartUtc,
    DateTime? EndUtc,
    string TimeZoneId,
    bool IsVirtual,
    bool IsCancelled,
    string AttendeeName,
    string Response,
    string? BrandingKey = null
)
{
    public static AppointmentRsvpView Of(
        Appointment appointment,
        AppointmentAttendee attendee,
        string? brandingKey = null
    )
    {
        var start = AppointmentEvents.FirstStartOf(appointment);
        var end = appointment.Timing.EndUtc ?? (start + appointment.Timing.Duration);
        return new AppointmentRsvpView(
            appointment.Title.Value,
            start,
            end,
            appointment.Timing.TimeZone.Id,
            appointment.IsVirtual,
            appointment.Status == AppointmentStatus.Cancelled,
            attendee.Snapshot.DisplayName,
            attendee.Response.ToString(),
            brandingKey
        );
    }
}
