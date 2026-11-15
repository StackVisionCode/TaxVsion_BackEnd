using BuildingBlocks.Messaging.CalendarIntegrationEvents;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary><c>calendar.public_booking_already_booked.v1</c>: a quien pidió otra demo teniendo una, su demo y un enlace de gestión nuevo.</summary>
public static class PublicBookingAlreadyBookedConsumer
{
    public static async Task Handle(
        PublicBookingAlreadyBookedIntegrationEvent evt,
        PublicBookingEmails emails,
        CancellationToken ct
    )
    {
        await emails.SendAlreadyBookedAsync(evt, ct);
    }
}
