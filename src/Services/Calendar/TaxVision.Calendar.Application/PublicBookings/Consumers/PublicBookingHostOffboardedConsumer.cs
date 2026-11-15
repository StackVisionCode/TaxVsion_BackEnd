using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using Wolverine;

namespace TaxVision.Calendar.Application.PublicBookings.Consumers;

/// <summary>
/// Retiro de un anfitrión de demos públicas (guardrail 38). Deja de atender en sus páginas (la que se queda sin
/// anfitriones se apaga) y cada demo futura suya pasa a otro anfitrión libre a la misma hora, con su enlace; si no
/// hay ninguno, la demo y su cita se cancelan. El visitante recibe el correo del cambio o de la cancelación.
/// La cita de una demo no la toca <c>CalendarUserOffboardedConsumer</c>: se cambia siempre desde su reserva.
/// </summary>
public static class PublicBookingHostOffboardedConsumer
{
    private const string HostChanged = "Your demo host changed.";
    private const string HostUnavailable = "Your demo host is no longer available.";

    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        IBookingPageRepository pages,
        IPublicBookingRepository bookings,
        IAppointmentRepository appointments,
        PublicBookingScheduler scheduler,
        PublicBookingNotices notices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<PublicBooking> logger,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        foreach (var page in await pages.ListByActiveHostAsync(msg.TenantId, msg.UserId, ct))
            page.RemoveHostOnOffboarding(msg.UserId, now);
        await unitOfWork.SaveChangesAsync(ct);

        var actor = msg.OffboardedByUserId ?? msg.UserId;
        var (moved, cancelled) = (0, 0);
        foreach (var booking in await bookings.ListUpcomingByHostAsync(msg.TenantId, msg.UserId, now, ct))
        {
            if (await HandOverAsync(booking, actor, now, pages, appointments, scheduler, notices, unitOfWork, bus, ct))
                moved++;
            else if (await CancelAsync(booking, actor, now, appointments, unitOfWork, ct))
                cancelled++;
        }

        if (moved + cancelled > 0)
            logger.LogInformation(
                "Offboarded demo host {UserId}: {Moved} public booking(s) handed over, {Cancelled} cancelled, in tenant {TenantId}.",
                msg.UserId,
                moved,
                cancelled,
                msg.TenantId
            );
    }

    /// <summary>A otro anfitrión libre a la misma hora. El enlace de gestión rota y va en el correo del cambio.</summary>
    private static async Task<bool> HandOverAsync(
        PublicBooking booking,
        Guid actor,
        DateTime now,
        IBookingPageRepository pages,
        IAppointmentRepository appointments,
        PublicBookingScheduler scheduler,
        PublicBookingNotices notices,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var page = await pages.GetByIdAsync(booking.TenantId, booking.BookingPageId, ct);
        if (page is null || booking.AppointmentId is not { } appointmentId)
            return false;
        var appointment = await appointments.GetByIdAsync(booking.TenantId, appointmentId, ct);
        if (appointment.IsFailure)
            return false;

        var start = booking.SlotStartUtc;
        var token = await scheduler.MoveAsync(page, booking, appointment.Value, start, now, ct, actor, HostChanged);
        if (token.IsFailure)
            return false;
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            await notices.RescheduledAsync(page, booking, start, PublicBookingActor.Staff, HostChanged, token.Value, ct)
        );
        return true;
    }

    private static async Task<bool> CancelAsync(
        PublicBooking booking,
        Guid actor,
        DateTime now,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (booking.AppointmentId is not { } appointmentId)
            return false;
        var appointment = await appointments.GetByIdAsync(booking.TenantId, appointmentId, ct);
        if (appointment.IsFailure || appointment.Value.CanChangeFromPublicPage().IsFailure)
            return false;
        if (booking.CancelByStaff(actor, HostUnavailable, now).IsFailure)
            return false;
        appointment.Value.CancelByPublicPageStaff(actor, HostUnavailable, now);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
