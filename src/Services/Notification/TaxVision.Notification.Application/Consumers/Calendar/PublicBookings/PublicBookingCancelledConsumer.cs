using BuildingBlocks.Messaging.CalendarIntegrationEvents;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary><c>calendar.public_booking_cancelled.v1</c>: la cancelación a las dos partes, con el .ics que la saca del calendario.</summary>
public static class PublicBookingCancelledConsumer
{
    public static async Task Handle(
        PublicBookingCancelledIntegrationEvent evt,
        PublicBookingEmails emails,
        CancellationToken ct
    )
    {
        await emails.SendCancelledAsync(evt, ct);
    }
}
