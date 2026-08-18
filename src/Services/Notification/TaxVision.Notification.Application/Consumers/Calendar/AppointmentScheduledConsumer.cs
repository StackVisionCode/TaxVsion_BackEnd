using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

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

    public static async Task Handle(
        AppointmentScheduledIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        IUserNotificationPreferenceRepository preferences,
        ILogger<AppointmentScheduledIntegrationEvent> logger,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var recipients = await CalendarRecipients.AllowedAsync(
            evt.Recipients,
            evt.TenantId,
            preferences,
            logger,
            TemplateKey,
            ct
        );

        if (recipients.Count == 0)
            return;

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var render = (
                await scribeClient.RenderAsync(
                    TemplateKey,
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["appointment_title"] = evt.Title,
                        ["start_local"] = CalendarTimeFormatting.InZone(evt.StartUtc, evt.TimeZoneId),
                        ["time_zone"] = evt.TimeZoneId,
                        ["is_recurring"] = evt.IsRecurring,
                        ["is_virtual"] = evt.IsVirtual,
                        ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered(TemplateKey);

            foreach (var email in recipients)
            {
                await gateway.QueueEmailAsync(
                    new EmailDispatchRequest(
                        TenantId: evt.TenantId,
                        To: email,
                        Subject: render.Subject,
                        HtmlBody: render.Html,
                        TextBody: render.Text ?? string.Empty,
                        TemplateKey: TemplateKey,
                        RelatedEventId: evt.EventId,
                        CorrelationId: correlation.CorrelationId,
                        InlineAssets: render.InlineAssets
                    ),
                    ct
                );
            }
        }
    }
}
