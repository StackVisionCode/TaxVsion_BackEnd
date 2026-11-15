using BuildingBlocks.Messaging.CalendarIntegrationEvents;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary><c>calendar.public_booking_rescheduled.v1</c>: el cambio de horario a las dos partes, con el .ics actualizado.</summary>
public static class PublicBookingRescheduledConsumer
{
    public static async Task Handle(
        PublicBookingRescheduledIntegrationEvent evt,
        PublicBookingEmails emails,
        CancellationToken ct
    )
    {
        await emails.SendRescheduledAsync(evt, ct);
    }
}
