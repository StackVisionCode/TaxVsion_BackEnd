using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

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
                "Accepted" => "Un asistente acepto la cita",
                "Declined" => "Un asistente no puede asistir",
                _ => "Un asistente respondio a la cita",
            };

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                title,
                title,
                NotificationCategory.Calendar,
                "calendar.attendee_responded",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.OrganizerUserId,
                ct: ct
            );
        }
    }
}
