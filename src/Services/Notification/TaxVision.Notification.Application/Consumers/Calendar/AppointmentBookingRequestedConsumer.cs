using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.booking_requested.v1</c>: un cliente pidió una cita en el portal. El preparador que pidió
/// (o el primero libre con "Any available") recibe in-app y push para aprobarla, asignarla o rechazarla.
/// </summary>
public static class AppointmentBookingRequestedConsumer
{
    public static string Heading(AppointmentBookingRequestedIntegrationEvent evt) =>
        $"Booking request from {(string.IsNullOrWhiteSpace(evt.CustomerName) ? "a client" : evt.CustomerName)}: {evt.Title}";

    public static async Task Handle(
        AppointmentBookingRequestedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        IReadOnlyList<AppointmentRecipient> office = [new AppointmentRecipient(string.Empty, evt.OrganizerUserId)];
        var heading = Heading(evt);
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await CalendarRecipients.PushAsync(
                dispatcher,
                office,
                evt.TenantId,
                heading,
                "Approve it, assign it to someone or decline it.",
                NotificationKinds.CalendarBookingRequested,
                evt.EventId,
                correlation.CorrelationId,
                ct
            );

            await CalendarRecipients.PublishInAppAsync(
                dispatcher,
                office,
                evt.TenantId,
                evt.AppointmentId,
                evt.EventId,
                NotificationKinds.CalendarBookingRequested,
                "calendar.booking_requested.v1",
                evt.OccurredOn,
                heading,
                correlation.CorrelationId,
                evt.CustomerId,
                ct
            );
        }
    }
}
