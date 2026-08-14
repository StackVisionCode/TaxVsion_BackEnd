using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

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
    private const string TemplateKey = "calendar.appointment_scheduled.v1";

    public static async Task Handle(
        AppointmentAttendeeAddedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        IUserNotificationPreferenceRepository preferences,
        ILogger<AppointmentAttendeeAddedIntegrationEvent> logger,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.Email is not { Length: > 0 } email)
            return;

        var recipients = await CalendarRecipients.AllowedAsync(
            [new AppointmentRecipient(email, evt.UserId)],
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
                        ["start_local"] = evt.StartUtc is { } start
                            ? CalendarTimeFormatting.InZone(start, evt.TimeZoneId)
                            : "según la serie",
                        ["time_zone"] = evt.TimeZoneId,
                        ["is_recurring"] = evt.IsRecurring,
                        ["is_virtual"] = evt.IsVirtual,
                        ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered(TemplateKey);

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: recipients[0],
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
