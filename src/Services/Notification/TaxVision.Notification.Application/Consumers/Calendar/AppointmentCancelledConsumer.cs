using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.appointment_cancelled.v1</c> — se cancelo la cita.
///
/// <para>
/// Es el aviso que mas importa de los cuatro: no avisar deja a alguien presentandose a una reunion que
/// no existe. Por eso sale aunque no haya motivo escrito.
/// </para>
/// </summary>
public static class AppointmentCancelledConsumer
{
    private const string TemplateKey = "calendar.appointment_cancelled.v1";

    public static async Task Handle(
        AppointmentCancelledIntegrationEvent evt,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        NotificationDispatcher dispatcher,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var recipients = evt.Recipients;
        if (recipients.Count == 0)
            return;

        // Un aviso por persona: con sala, los invitados reciben el de Communication.
        var direct = CalendarRecipients.WithoutRoomInvitees(
            evt.Recipients,
            evt.MeetingId is not null,
            evt.OrganizerUserId
        );

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await CalendarRecipients.PushAsync(
                dispatcher,
                locales,
                evt.Recipients,
                evt.TenantId,
                NotificationKinds.CalendarAppointmentCancelled,
                evt.AppointmentId,
                evt.EventId,
                correlation.CorrelationId,
                ct
            );

            await CalendarRecipients.PublishInAppAsync(
                dispatcher,
                direct,
                evt.TenantId,
                evt.AppointmentId,
                evt.EventId,
                NotificationKinds.CalendarAppointmentCancelled,
                "calendar.appointment_cancelled.v1",
                evt.OccurredOn,
                "Appointment cancelled",
                correlation.CorrelationId,
                customerId: null,
                ct
            );

            await CalendarRecipients.QueueByAudienceAsync(
                direct,
                evt.TenantId,
                TemplateKey,
                NotificationKinds.CalendarAppointmentCancelled,
                _ => new Dictionary<string, object?>
                {
                    ["scope"] = evt.Scope,
                    // Qué se canceló: sin esto, en una serie semanal el cliente no sabe cuál.
                    ["start_local"] = ScribeDates.Instant(evt.StartUtc),
                    ["time_zone"] = evt.TimeZoneId,
                    ["reason"] = evt.Reason,
                    ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                },
                scribeClient,
                dispatcher,
                locales,
                evt.EventId,
                correlation.CorrelationId,
                ct,
                timeZoneId: evt.TimeZoneId
            );
        }
    }
}
