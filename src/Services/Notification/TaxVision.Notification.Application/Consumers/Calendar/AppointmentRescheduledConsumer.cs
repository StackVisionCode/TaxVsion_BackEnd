using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

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
        IUserNotificationPreferenceRepository preferences,
        NotificationDispatcher dispatcher,
        ILogger<AppointmentRescheduledIntegrationEvent> logger,
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
            await CalendarRecipients.PublishInAppAsync(
                dispatcher,
                evt.Recipients,
                evt.TenantId,
                evt.AppointmentId,
                evt.EventId,
                NotificationKinds.CalendarAppointmentRescheduled,
                "calendar.appointment_rescheduled.v1",
                evt.OccurredOn,
                "Cita reprogramada",
                correlation.CorrelationId,
                customerId: null,
                ct
            );

            var render = (
                await scribeClient.RenderAsync(
                    TemplateKey,
                    evt.TenantId,
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
                    ct
                )
            ).EnsureRendered(TemplateKey);

            foreach (var email in recipients)
            {
                await gateway.QueueEmailAsync(
                    render.ToDispatchRequest(
                        tenantId: evt.TenantId,
                        to: email,
                        templateKey: TemplateKey,
                        relatedEventId: evt.EventId,
                        correlationId: correlation.CorrelationId
                    ),
                    ct
                );
            }
        }
    }
}
