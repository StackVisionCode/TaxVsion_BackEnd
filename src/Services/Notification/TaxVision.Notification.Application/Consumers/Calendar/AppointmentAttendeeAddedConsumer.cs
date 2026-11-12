using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.attendee_added.v1</c> — se sumó un asistente y hay que invitarlo.
///
/// <para>
/// Éste es el que manda la invitación de verdad. La cita nace sin asistentes y se le agregan después,
/// así que el evento de la cita agendada sale cuando la lista todavía está vacía; sin este consumer no
/// se invitaba a nadie, nunca, y no fallaba nada.
/// </para>
///
/// <para>
/// Usa la misma plantilla que la cita agendada: para quien lo recibe es la misma invitación, y tener
/// dos textos para lo mismo sólo garantiza que uno de los dos envejezca.
/// </para>
/// </summary>
public static class AppointmentAttendeeAddedConsumer
{
    private const string TemplateKey = "calendar.appointment_attendee_added.v1";

    public static async Task Handle(
        AppointmentAttendeeAddedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hosts,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.Email is not { Length: > 0 } email)
            return;

        IReadOnlyList<AppointmentRecipient> recipients =
        [
            new AppointmentRecipient(email, evt.UserId, evt.CustomerId, evt.DisplayName, evt.RsvpToken),
        ];

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await CalendarRecipients.PushAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                $"You were added to an appointment: {evt.Title}",
                "Open your calendar for the details.",
                NotificationKinds.CalendarAttendeeAdded,
                evt.EventId,
                correlation.CorrelationId,
                ct
            );

            // En una cita virtual la invitación de la sala (Communication) ya lleva in-app y correo.
            if (evt.IsVirtual)
                return;

            await CalendarRecipients.PublishInAppAsync(
                dispatcher,
                [new AppointmentRecipient(email, evt.UserId)],
                evt.TenantId,
                evt.AppointmentId,
                evt.EventId,
                NotificationKinds.CalendarAttendeeAdded,
                "calendar.attendee_added.v1",
                evt.OccurredOn,
                $"You were added to an appointment: {evt.Title}",
                correlation.CorrelationId,
                evt.CustomerId,
                ct
            );

            await CalendarRecipients.QueueByAudienceAsync(
                recipients,
                evt.TenantId,
                TemplateKey,
                NotificationKinds.CalendarAttendeeAdded,
                new Dictionary<string, object?>
                {
                    ["appointment_title"] = evt.Title,
                    ["start_local"] = evt.StartUtc is { } start
                        ? CalendarTimeFormatting.InZone(start, evt.TimeZoneId)
                        : "Follows the series schedule",
                    ["time_zone"] = evt.TimeZoneId,
                    ["is_recurring"] = evt.IsRecurring,
                    ["is_virtual"] = evt.IsVirtual,
                    ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                },
                scribeClient,
                gateway,
                evt.EventId,
                correlation.CorrelationId,
                ct,
                TenantEmailLinks.StaffBase(await hosts.ResolveHostAsync(evt.TenantId, ct), portal.Value)
            );
        }
    }
}
