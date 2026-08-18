using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
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
