using System.Globalization;
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
        IScribeRenderClient scribeClient,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        ILogger<MeetingReminderIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var meetingTitle = MeetingNotifications.Title(evt.MeetingTitle);
            var when = MeetingNotifications.Describe(evt.ScheduledForUtc, evt.TimeZone);

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
                $"{when}.",
                correlation.CorrelationId,
                context,
                ct,
                new Dictionary<string, string>
                {
                    ["meetingTitle"] = meetingTitle,
                    ["minutesBefore"] = evt.MinutesBefore.ToString(CultureInfo.InvariantCulture),
                }
            );

            var emails = evt
                .InvitedEmails.Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (emails.Count == 0)
                return;

            var locale = MeetingNotifications.ExternalLocale(locales, evt.TimeZone);
            var render = (
                await scribeClient.RenderForRecipientAsync(
                    EventKey,
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["meeting_title"] = MeetingNotifications.EmailTitle(evt.MeetingTitle, locale),
                        ["start_local"] = ScribeDates.Instant(evt.ScheduledForUtc),
                        ["time_zone"] = MeetingNotifications.TimeZoneLabel(
                            evt.TimeZone,
                            evt.ScheduledForUtc,
                            locale.Language
                        ),
                        ["duration_minutes"] = evt.DurationMinutes,
                        ["minutes_before"] = evt.MinutesBefore,
                        ["host_name"] = evt.HostDisplayName,
                        ["meeting_code"] = evt.ShortCode,
                    },
                    ScribeRenderAudience.Client,
                    locale,
                    ct
                )
            ).EnsureRendered(EventKey);

            foreach (var email in emails)
            {
                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CommunicationMeetingReminder,
                        evt.TenantId,
                        // Invitado externo: sin usuario no hay preferencia; idioma y zona ya resueltos.
                        new NotificationTarget(Email: email, Locale: locale),
                        evt.EventId,
                        EventKey,
                        evt.OccurredOn,
                        correlation.CorrelationId,
                        Email: MeetingNotifications.Rendered(EventKey, render, null)
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
