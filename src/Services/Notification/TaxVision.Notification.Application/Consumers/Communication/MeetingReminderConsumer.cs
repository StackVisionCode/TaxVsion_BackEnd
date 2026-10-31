using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// <c>communication.meeting.reminder.v1</c> — la reunión empieza en <c>MinutesBefore</c> minutos. Correo a
/// los externos sin cuenta; in-app a participantes e invitados con cuenta (el CRM y el portal también
/// reciben el socket <c>meeting.reminder</c>, esto es lo que queda en la bandeja).
///
/// <para>El externo no recibe enlace: el suyo es el de su invitación (con token) y no viaja en este
/// evento. El correo le recuerda usarlo y le da el código. Los enlaces de staff y portal van al in-app
/// como contexto.</para>
/// </summary>
public static class MeetingReminderConsumer
{
    private const string EventKey = "communication.meeting.reminder.v1";

    public static async Task Handle(
        MeetingReminderIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICorrelationContext correlation,
        ILogger<MeetingReminderIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var meetingTitle = MeetingNotifications.Title(evt.MeetingTitle);
            var startLocal = MeetingNotifications.LocalTime(evt.ScheduledForUtc, evt.TimeZone);
            var timeZone = MeetingNotifications.TimeZoneLabel(evt.TimeZone);

            var context = new Dictionary<string, string>
            {
                ["scheduledForUtc"] = evt.ScheduledForUtc.ToString("O"),
                ["minutesBefore"] = evt.MinutesBefore.ToString(),
            };
            if (!string.IsNullOrWhiteSpace(evt.StaffJoinUrl))
                context["staffJoinUrl"] = evt.StaffJoinUrl;
            if (!string.IsNullOrWhiteSpace(evt.PortalJoinUrl))
                context["portalJoinUrl"] = evt.PortalJoinUrl;

            await MeetingNotifications.PublishInAppAsync(
                dispatcher,
                evt.ParticipantUserIds.Concat(evt.InviteeUserIds),
                exclude: null,
                evt.TenantId,
                evt.MeetingId,
                evt.EventId,
                NotificationKinds.CommunicationMeetingReminder,
                EventKey,
                evt.OccurredOn,
                $"\"{meetingTitle}\" starts in {evt.MinutesBefore} min",
                $"{startLocal} ({timeZone}).",
                correlation.CorrelationId,
                context,
                ct
            );

            var emails = evt
                .InvitedEmails.Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (emails.Count == 0)
                return;

            var render = (
                await scribeClient.RenderAsync(
                    EventKey,
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["meeting_title"] = meetingTitle,
                        ["start_local"] = startLocal,
                        ["time_zone"] = timeZone,
                        ["duration_minutes"] = evt.DurationMinutes,
                        ["minutes_before"] = evt.MinutesBefore,
                        ["host_name"] = evt.HostDisplayName,
                        ["meeting_code"] = evt.ShortCode,
                    },
                    ct
                )
            ).EnsureRendered(EventKey);

            foreach (var email in emails)
            {
                await gateway.QueueEmailAsync(
                    render.ToDispatchRequest(
                        tenantId: evt.TenantId,
                        to: email,
                        templateKey: EventKey,
                        relatedEventId: evt.EventId,
                        correlationId: correlation.CorrelationId
                    ),
                    ct
                );
            }

            logger.LogInformation(
                "Meeting {MeetingId} reminder emailed to {Count} external invitee(s).",
                evt.MeetingId,
                emails.Count
            );
        }
    }
}
