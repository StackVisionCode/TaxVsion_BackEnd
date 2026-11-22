using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// <c>communication.meeting.rescheduled.v1</c> — la reunión se movió. El correo a los externos lleva la
/// hora vieja y la nueva (quien tiene ocho reuniones esa semana no recuerda cuál era) y un
/// <c>invite.ics</c> con el mismo <c>UID</c> y un <c>SEQUENCE</c> mayor, que es lo que hace que el
/// calendario mueva la cita en vez de duplicarla. In-app a participantes e invitados con cuenta.
/// </summary>
public static class MeetingRescheduledConsumer
{
    private const string EventKey = "communication.meeting.rescheduled.v1";

    public static async Task Handle(
        MeetingRescheduledIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IScribeRenderClient scribeClient,
        ICalendarInviteBuilder invites,
        IPlatformSettings platform,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        ILogger<MeetingRescheduledIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var meetingTitle = MeetingNotifications.Title(evt.MeetingTitle);
            var newWhen = MeetingNotifications.Describe(evt.NewScheduledForUtc, evt.TimeZone);

            var context = new Dictionary<string, string>();
            if (evt.NewScheduledForUtc is { } newUtc)
                context["scheduledForUtc"] = newUtc.ToString("O");

            await MeetingNotifications.PublishInAppAsync(
                dispatcher,
                evt.ParticipantUserIds.Concat(evt.InviteeUserIds),
                exclude: evt.RescheduledByUserId,
                evt.TenantId,
                evt.MeetingId,
                evt.EventId,
                NotificationKinds.CommunicationMeetingRescheduled,
                EventKey,
                evt.OccurredOn,
                $"\"{meetingTitle}\" was rescheduled",
                newWhen is null ? null : $"New time: {newWhen}.",
                correlation.CorrelationId,
                context,
                ct,
                new Dictionary<string, string>(context) { ["meetingTitle"] = meetingTitle }
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
                        ["previous_local"] = ScribeDates.Instant(evt.PreviousScheduledForUtc),
                        ["new_local"] = ScribeDates.Instant(evt.NewScheduledForUtc),
                        ["time_zone"] = MeetingNotifications.TimeZoneLabel(
                            evt.TimeZone,
                            evt.NewScheduledForUtc,
                            locale.Language
                        ),
                        ["duration_minutes"] = evt.DurationMinutes,
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
                var invite = evt.NewScheduledForUtc is { } startUtc
                    ? invites.Build(
                        MeetingNotifications.Invite(
                            CalendarInviteMethod.Request,
                            evt.MeetingId,
                            evt.MeetingTitle,
                            startUtc,
                            evt.DurationMinutes,
                            sequence: MeetingNotifications.SequenceAt(evt.RescheduledAtUtc),
                            stampUtc: evt.RescheduledAtUtc,
                            url: null,
                            meetingCode: evt.ShortCode,
                            hostDisplayName: evt.HostDisplayName,
                            platform,
                            attendeeEmail: email,
                            attendeeName: null
                        )
                    )
                    : null;

                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CommunicationMeetingRescheduled,
                        evt.TenantId,
                        // Invitado externo: sin usuario no hay preferencia; idioma y zona ya resueltos.
                        new NotificationTarget(Email: email, Locale: locale),
                        evt.EventId,
                        EventKey,
                        evt.OccurredOn,
                        correlation.CorrelationId,
                        Email: MeetingNotifications.Rendered(EventKey, render, invite)
                    ),
                    ct
                );
            }

            logger.LogInformation(
                "Meeting {MeetingId} reschedule emailed to {Count} external invitee(s).",
                evt.MeetingId,
                emails.Count
            );
        }
    }
}
