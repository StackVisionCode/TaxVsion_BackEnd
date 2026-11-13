using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>Una reserva por tenant e id: así la referencian los jobs, que barren todos los tenants.</summary>
public sealed record PublicBookingRef(Guid TenantId, Guid BookingId);

/// <summary>
/// Lo que hace cada job con una reserva. Cada paso corre en su propia unidad (BB-3): carga, decide con el dominio y
/// devuelve si hubo algo que guardar. La unidad guarda y publica junto, o nada.
/// </summary>
public static class PublicBookingMaintenance
{
    /// <summary>Cierra una solicitud cuya retención venció: el horario vuelve a estar libre.</summary>
    public static async Task<bool> ExpireHoldAsync(
        PublicBookingRef target,
        IPublicBookingRepository bookings,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var booking = await bookings.GetByIdAsync(target.TenantId, target.BookingId, ct);
        return booking is not null && booking.Expire(nowUtc).IsSuccess;
    }

    /// <summary>
    /// El recordatorio de la demo, una sola vez, con un enlace de gestión nuevo ya cifrado. Null si no corresponde
    /// (la página no los manda, no es la hora, ya salió o la demo se reservó dentro de la ventana).
    /// </summary>
    public static async Task<PublicBookingReminderDueIntegrationEvent?> RemindAsync(
        PublicBookingRef target,
        IPublicBookingRepository bookings,
        IBookingPageRepository pages,
        PublicBookingNotices notices,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var booking = await bookings.GetByIdAsync(target.TenantId, target.BookingId, ct);
        var page = booking is null ? null : await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (booking is null || page?.ReminderHoursBefore is not { } hours)
            return null;

        var token = booking.MarkReminderSent(hours, nowUtc);
        return token.IsFailure ? null : await notices.ReminderAsync(page, booking, hours, token.Value, ct);
    }

    /// <summary>
    /// Retención (D-04): una solicitud que nunca se confirmó se borra; una demo cerrada se anonimiza, y su cita
    /// olvida quién era el visitante. Lo vigente o reciente no se toca.
    /// </summary>
    public static async Task<bool> PurgeAsync(
        PublicBookingRef target,
        IPublicBookingRepository bookings,
        IAppointmentRepository appointments,
        PublicBookingOptions options,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var booking = await bookings.GetByIdAsync(target.TenantId, target.BookingId, ct);
        if (booking is null)
            return false;

        if (
            booking.Status == PublicBookingStatus.Expired
            && booking.UpdatedAtUtc <= nowUtc.AddHours(-options.UnverifiedRetentionHours)
        )
        {
            bookings.Remove(booking);
            return true;
        }

        if (
            !booking.IsClosed
            || booking.AnonymizedAtUtc is not null
            || booking.UpdatedAtUtc > nowUtc.AddMonths(-options.ClosedRetentionMonths)
        )
            return false;
        if (booking.Anonymize(nowUtc).IsFailure)
            return false;
        if (booking.AppointmentId is { } appointmentId)
        {
            var appointment = await appointments.GetByIdAsync(booking.TenantId, appointmentId, ct);
            if (appointment.IsSuccess)
                appointment.Value.ForgetPublicVisitor(NeutralTitle());
        }
        return true;
    }

    private static AppointmentTitle NeutralTitle() => AppointmentTitle.Create("Product demo").Value;
}
