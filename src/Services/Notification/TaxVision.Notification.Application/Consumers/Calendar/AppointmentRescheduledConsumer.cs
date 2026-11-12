using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.appointment_rescheduled.v1</c> — la cita se movio.
///
/// <para>
/// El aviso lleva la hora vieja y la nueva. Decir solo la nueva obliga al que lo lee a recordar cual
/// era, y quien tiene ocho citas esa semana no la recuerda.
/// </para>
///
/// <para>
/// Solo se avisa del alcance que cambia la hora de todos. Mover <b>una</b> ocurrencia de una serie
/// tambien llega aqui, pero con su instante original: sin el, el destinatario no sabria cual de las
/// ocho se movio.
/// </para>
/// </summary>
public static class AppointmentRescheduledConsumer
{
    private const string TemplateKey = "calendar.appointment_rescheduled.v1";

    public static async Task Handle(
        AppointmentRescheduledIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hosts,
        NotificationDispatcher dispatcher,
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
                evt.Recipients,
                evt.TenantId,
                "Appointment rescheduled",
                "The time changed. Open your calendar.",
                NotificationKinds.CalendarAppointmentRescheduled,
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
                NotificationKinds.CalendarAppointmentRescheduled,
                "calendar.appointment_rescheduled.v1",
                evt.OccurredOn,
                "Appointment rescheduled",
                correlation.CorrelationId,
                customerId: null,
                ct
            );

            await CalendarRecipients.QueueByAudienceAsync(
                direct,
                evt.TenantId,
                TemplateKey,
                NotificationKinds.CalendarAppointmentRescheduled,
                new Dictionary<string, object?>
                {
                    ["scope"] = evt.Scope,
                    ["previous_local"] = evt.PreviousStartUtc is { } previous
                        ? CalendarTimeFormatting.InZone(previous, evt.TimeZoneId)
                        : null,
                    ["new_local"] = CalendarTimeFormatting.InZone(evt.NewStartUtc, evt.TimeZoneId),
                    ["time_zone"] = evt.TimeZoneId,
                    ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                },
                scribeClient,
                gateway,
                evt.EventId,
                correlation.CorrelationId,
                ct,
                TenantEmailLinks.StaffBase(await hosts.ResolveHostAsync(evt.TenantId, ct), portal.Value),
                evt.OrganizerUserId
            );
        }
    }
}
