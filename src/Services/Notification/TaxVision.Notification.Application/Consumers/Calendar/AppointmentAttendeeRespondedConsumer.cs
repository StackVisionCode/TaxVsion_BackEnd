using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.attendee_responded.v1</c> — alguien contesto la invitacion.
///
/// <para>
/// Va al organizador y a nadie mas, in-app y no por correo: es seguimiento, y quien organiza una
/// reunion de seis no quiere seis correos diciendo «acepto».
/// </para>
/// </summary>
public static class AppointmentAttendeeRespondedConsumer
{
    public static async Task Handle(
        AppointmentAttendeeRespondedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var title = evt.Response switch
            {
                "Accepted" => "An attendee accepted the appointment",
                "Declined" => "An attendee can't make it",
                _ => "An attendee replied to the appointment",
            };

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CalendarAttendeeResponded,
                    evt.TenantId,
                    new NotificationTarget(evt.OrganizerUserId),
                    evt.EventId,
                    "calendar.attendee_responded.v1",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: new Dictionary<string, string> { ["response"] = evt.Response },
                        ResourceId: evt.AppointmentId.ToString(),
                        FallbackTitle: title
                    )
                ),
                ct
            );
        }
    }
}
