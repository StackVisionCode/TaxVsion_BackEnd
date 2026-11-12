using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.appointment_scheduled.v1</c> — hay una cita nueva y hay que invitar a los asistentes.
///
/// <para>
/// Los correos vienen <b>dentro del evento</b>, no de un directorio. Calendar guarda el nombre y el
/// correo del asistente como copia del día de la cita, así que aquí no hace falta resolver un
/// <c>userId</c> contra nada — que es justo lo que dejó sin correos a Reminder, porque Notification no
/// tiene directorio de usuarios.
/// </para>
///
/// <para>
/// La hora va en la zona de la cita y con la zona escrita al lado. No se convierte a la de cada
/// destinatario porque no existe: un asistente externo no tiene perfil, y adivinarla produciría un
/// correo con una hora equivocada y con pinta de correcta.
/// </para>
/// </summary>
public static class AppointmentScheduledConsumer
{
    private const string TemplateKey = "calendar.appointment_scheduled.v1";

    /// <summary>
    /// Tipo y título del push/in-app. Una reserva del cliente en el portal se anuncia como tal, con su
    /// nombre: para el preparador no es lo mismo que una cita que agendó él.
    /// </summary>
    public static (string Kind, string Title) Announcement(AppointmentScheduledIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.BookedByCustomerName)
            ? (NotificationKinds.CalendarAppointmentScheduled, $"New appointment: {evt.Title}")
            : (
                NotificationKinds.CalendarAppointmentBookedOnline,
                $"Booked online by {evt.BookedByCustomerName}: {evt.Title}"
            );

    public static async Task Handle(
        AppointmentScheduledIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ITenantHostResolver hosts,
        NotificationDispatcher dispatcher,
        IRecipientResolver recipientResolver,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var recipients = evt.Recipients;
        if (recipients.Count == 0)
            return;

        // Un aviso por persona: con sala, los invitados reciben el de Communication.
        var direct = CalendarRecipients.WithoutRoomInvitees(evt.Recipients, evt.IsVirtual, evt.OrganizerUserId);

        var (kind, title) = Announcement(evt);
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await CalendarRecipients.PushAsync(
                dispatcher,
                evt.Recipients,
                evt.TenantId,
                title,
                "Open your calendar for the details.",
                kind,
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
                kind,
                "calendar.appointment_scheduled.v1",
                evt.OccurredOn,
                title,
                correlation.CorrelationId,
                evt.CustomerId,
                ct
            );

            await CalendarRecipients.QueueByAudienceAsync(
                direct,
                evt.TenantId,
                TemplateKey,
                NotificationKinds.CalendarAppointmentScheduled,
                new Dictionary<string, object?>
                {
                    ["appointment_title"] = evt.Title,
                    ["start_local"] = CalendarTimeFormatting.InZone(evt.StartUtc, evt.TimeZoneId),
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
                TenantEmailLinks.StaffBase(await hosts.ResolveHostAsync(evt.TenantId, ct), portal.Value),
                evt.OrganizerUserId
            );

            // Aprobada desde una solicitud del portal: el cliente lo ve en vivo, sin recargar.
            if (evt.ApprovedRequest && evt.CustomerId is { } customerId)
                await CalendarClientNotices.SendAsync(
                    dispatcher,
                    recipientResolver,
                    evt.TenantId,
                    customerId,
                    evt.AppointmentId,
                    evt.EventId,
                    NotificationKinds.CalendarBookingConfirmed,
                    $"Your appointment is confirmed: {evt.Title}",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    ct
                );
        }
    }
}
