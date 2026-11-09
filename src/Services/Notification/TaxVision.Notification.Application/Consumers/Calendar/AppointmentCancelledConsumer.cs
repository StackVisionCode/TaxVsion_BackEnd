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
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var recipients = evt.Recipients;
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
                NotificationKinds.CalendarAppointmentCancelled,
                "calendar.appointment_cancelled.v1",
                evt.OccurredOn,
                "Cita cancelada",
                correlation.CorrelationId,
                customerId: null,
                ct
            );

            await CalendarRecipients.QueueByAudienceAsync(
                recipients,
                evt.TenantId,
                TemplateKey,
                NotificationKinds.CalendarAppointmentCancelled,
                new Dictionary<string, object?>
                {
                    ["scope"] = evt.Scope,
                    ["reason"] = evt.Reason,
                    ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                },
                scribeClient,
                gateway,
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
