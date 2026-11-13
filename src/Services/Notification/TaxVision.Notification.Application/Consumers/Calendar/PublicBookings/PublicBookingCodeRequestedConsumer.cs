using BuildingBlocks.Messaging.CalendarIntegrationEvents;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary><c>calendar.public_booking_code_requested.v1</c>: el código de 6 dígitos al visitante (confidencial).</summary>
public static class PublicBookingCodeRequestedConsumer
{
    public static async Task Handle(
        PublicBookingCodeRequestedIntegrationEvent evt,
        PublicBookingEmails emails,
        CancellationToken ct
    )
    {
        await emails.SendCodeAsync(evt, ct);
    }
}
