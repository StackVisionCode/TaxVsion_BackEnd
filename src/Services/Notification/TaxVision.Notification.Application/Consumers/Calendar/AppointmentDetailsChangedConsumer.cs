using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// <c>calendar.appointment_updated.v1</c>: cambiaron título, descripción o lugar. Avisa a todos los
/// asistentes menos a quien editó, con o sin sala: la sala no manda nada por este cambio. El nombre viejo
/// va al lado para que se reconozca la cita.
/// </summary>
public static class AppointmentDetailsChangedConsumer
{
    private const string TemplateKey = "calendar.appointment_updated.v1";

    public static async Task Handle(
        AppointmentDetailsChangedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var recipients = new List<AppointmentRecipient>();
        foreach (var recipient in evt.Recipients)
        {
            if (recipient.UserId is null || recipient.UserId != evt.ChangedByUserId)
                recipients.Add(recipient);
        }

        if (recipients.Count == 0)
            return;

        var titleChanged = evt.ChangedFields.Contains("Title");
        var heading = titleChanged ? $"{evt.PreviousTitle} is now {evt.Title}" : $"Appointment updated: {evt.Title}";

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await CalendarRecipients.PushAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                heading,
                "Open your calendar for the details.",
                NotificationKinds.CalendarAppointmentUpdated,
                evt.EventId,
                correlation.CorrelationId,
                ct
            );

            await CalendarRecipients.PublishInAppAsync(
                dispatcher,
                recipients,
                evt.TenantId,
                evt.AppointmentId,
                evt.EventId,
                NotificationKinds.CalendarAppointmentUpdated,
                TemplateKey,
                evt.OccurredOn,
                heading,
                correlation.CorrelationId,
                customerId: null,
                ct
            );

            await CalendarRecipients.QueueByAudienceAsync(
                recipients,
                evt.TenantId,
                TemplateKey,
                NotificationKinds.CalendarAppointmentUpdated,
                new Dictionary<string, object?>
                {
                    ["appointment_title"] = evt.Title,
                    ["previous_title"] = titleChanged ? evt.PreviousTitle : null,
                    ["title_changed"] = titleChanged,
                    ["location"] = evt.Location,
                    ["location_changed"] = evt.ChangedFields.Contains("Location"),
                    ["description"] = evt.Description,
                    ["description_changed"] = evt.ChangedFields.Contains("Description"),
                    ["start_local"] = evt.StartUtc is { } start
                        ? CalendarTimeFormatting.InZone(start, evt.TimeZoneId)
                        : "Follows the series schedule",
                    ["time_zone"] = evt.TimeZoneId,
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
