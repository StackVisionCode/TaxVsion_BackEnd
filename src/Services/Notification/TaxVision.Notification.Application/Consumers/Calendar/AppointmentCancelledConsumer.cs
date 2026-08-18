using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

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
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        IUserNotificationPreferenceRepository preferences,
        ILogger<AppointmentCancelledIntegrationEvent> logger,
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
                        ["reason"] = evt.Reason,
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
