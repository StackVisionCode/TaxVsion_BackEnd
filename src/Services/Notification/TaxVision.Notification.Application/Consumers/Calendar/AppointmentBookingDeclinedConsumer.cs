using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.booking_declined.v1</c>: la oficina rechazó la solicitud. El cliente recibe un correo con
/// la hora que pidió y el motivo, para pedir otra.
/// </summary>
public static class AppointmentBookingDeclinedConsumer
{
    private const string TemplateKey = "calendar.booking_declined.v1";

    public static async Task Handle(
        AppointmentBookingDeclinedIntegrationEvent evt,
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
        if (evt.CustomerId is { } customerId)
        {
            using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
                await CalendarClientNotices.SendAsync(
                    dispatcher,
                    recipientResolver,
                    evt.TenantId,
                    customerId,
                    evt.AppointmentId,
                    evt.EventId,
                    NotificationKinds.CalendarBookingDeclined,
                    $"We couldn't confirm: {evt.Title}",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    ct
                );
        }

        if (evt.Recipients.Count == 0)
            return;

        // "Book another time": a reservar en el portal de la oficina.
        var bookAgain =
            $"{TenantEmailLinks.ClientBase(await hosts.ResolveHostAsync(evt.TenantId, ct), portal.Value)}/client/calendar/book";

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await CalendarRecipients.QueueByAudienceAsync(
                evt.Recipients,
                evt.TenantId,
                TemplateKey,
                NotificationKinds.CalendarBookingDeclined,
                new Dictionary<string, object?>
                {
                    ["appointment_title"] = evt.Title,
                    ["start_local"] = evt.StartUtc is { } start
                        ? CalendarTimeFormatting.InZone(start, evt.TimeZoneId)
                        : null,
                    ["time_zone"] = evt.TimeZoneId,
                    ["reason"] = string.IsNullOrWhiteSpace(evt.Reason) ? null : evt.Reason,
                    ["portal_link"] = bookAgain,
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
