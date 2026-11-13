using BuildingBlocks.Messaging.CalendarIntegrationEvents;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary><c>calendar.public_booking_confirmed.v1</c>: la confirmación al visitante y el aviso al anfitrión, cada uno con el .ics.</summary>
public static class PublicBookingConfirmedConsumer
{
    public static async Task Handle(
        PublicBookingConfirmedIntegrationEvent evt,
        PublicBookingEmails emails,
        CancellationToken ct
    )
    {
        await emails.SendConfirmedAsync(evt, ct);
    }
}
