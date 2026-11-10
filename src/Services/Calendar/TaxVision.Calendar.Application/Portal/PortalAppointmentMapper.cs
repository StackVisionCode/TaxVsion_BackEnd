using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Portal;

/// <summary>Arma lo que ve el cliente. Nombres de tipo y preparador se resuelven una vez por llamada.</summary>
internal sealed class PortalAppointmentMapper(
    IAppointmentTypeRepository types,
    IStaffContactClient contacts,
    BookingPolicy? policy
)
{
    private readonly Dictionary<Guid, string?> _typeNames = [];
    private readonly Dictionary<Guid, string> _preparers = [];

    /// <summary>
    /// Mover y cancelar en línea: con la reserva activa, una cita puntual, antes del plazo de la oficina.
    /// Una serie se cambia con la oficina.
    /// </summary>
    public static bool CanChange(Appointment appointment, BookingPolicy? policy, DateTime startUtc, DateTime nowUtc) =>
        appointment.Status != AppointmentStatus.Requested
        && policy is { Enabled: true }
        && appointment.Status != AppointmentStatus.Cancelled
        && !appointment.IsRecurring
        && startUtc - nowUtc >= TimeSpan.FromMinutes(policy.CustomerChangeCutoffMinutes);

    public async Task<PortalAppointmentResponse> MapAsync(
        Appointment appointment,
        Occurrence? occurrence,
        Guid customerId,
        CancellationToken ct
    )
    {
        var start = occurrence?.StartUtc ?? appointment.Timing.StartUtc ?? DateTime.UtcNow;
        var end = occurrence?.EndUtc ?? appointment.Timing.EndUtc ?? start;
        string? response = null;
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.CustomerId == customerId)
                response = attendee.Response.ToString();
        }

        var canChange = CanChange(appointment, policy, start, DateTime.UtcNow);
        // Una solicitud pendiente la puede retirar cuando quiera; moverla es cancelar y pedir otra.
        var isRequest = appointment.Status == AppointmentStatus.Requested;
        return new PortalAppointmentResponse(
            appointment.Id,
            occurrence is not null && appointment.IsRecurring ? occurrence.OriginalStartUtc : null,
            start,
            end,
            appointment.Timing.TimeZone.Id,
            occurrence?.Title ?? appointment.Title.Value,
            await TypeNameAsync(appointment, ct),
            occurrence?.Location ?? appointment.Location?.Value,
            appointment.IsVirtual,
            appointment.MeetingId,
            await PreparerAsync(appointment, ct),
            appointment.Status.ToString(),
            response,
            canChange || isRequest,
            canChange,
            appointment.AppointmentTypeId,
            appointment.OrganizerUserId
        );
    }

    private async Task<string?> TypeNameAsync(Appointment appointment, CancellationToken ct)
    {
        if (_typeNames.TryGetValue(appointment.AppointmentTypeId, out var cached))
            return cached;
        var type = await types.GetByIdAsync(appointment.TenantId, appointment.AppointmentTypeId, ct);
        var name = type.IsSuccess ? type.Value.Name : null;
        _typeNames[appointment.AppointmentTypeId] = name;
        return name;
    }

    /// <summary>El nombre del organizador como asistente; si no, el de Auth; si no, genérico.</summary>
    private async Task<string> PreparerAsync(Appointment appointment, CancellationToken ct)
    {
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId == appointment.OrganizerUserId)
                return attendee.Snapshot.DisplayName;
        }

        if (_preparers.TryGetValue(appointment.OrganizerUserId, out var cached))
            return cached;
        var contact = await contacts.FindAsync(appointment.TenantId, appointment.OrganizerUserId, ct);
        var name = string.IsNullOrWhiteSpace(contact?.DisplayName) ? "Your preparer" : contact.DisplayName;
        _preparers[appointment.OrganizerUserId] = name;
        return name;
    }
}
