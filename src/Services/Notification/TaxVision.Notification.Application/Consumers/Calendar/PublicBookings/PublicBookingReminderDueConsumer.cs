using BuildingBlocks.Messaging.CalendarIntegrationEvents;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary><c>calendar.public_booking_reminder_due.v1</c>: el recordatorio al visitante, con el enlace de la reunión y uno de gestión nuevo.</summary>
public static class PublicBookingReminderDueConsumer
{
    public static async Task Handle(
        PublicBookingReminderDueIntegrationEvent evt,
        PublicBookingEmails emails,
        CancellationToken ct
    )
    {
        await emails.SendReminderAsync(evt, ct);
    }
}
