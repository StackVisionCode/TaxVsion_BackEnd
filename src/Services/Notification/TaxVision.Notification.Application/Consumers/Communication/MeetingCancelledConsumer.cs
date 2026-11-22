using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// <c>communication.meeting.cancelled.v1</c> — se canceló la reunión. Correo (con <c>cancel.ics</c>,
/// <c>METHOD:CANCEL</c>, mismo <c>UID</c>) a los externos sin cuenta; in-app a participantes e invitados
/// con cuenta, menos quien canceló. Como en Calendar, es el aviso que más importa: sale aunque no haya
/// motivo escrito. Sin <c>ContactPolicy</c>: el evento no trae <c>customerId</c>.
/// </summary>
public static class MeetingCancelledConsumer
{
    private const string EventKey = "communication.meeting.cancelled.v1";

    public static async Task Handle(
        MeetingCancelledIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IScribeRenderClient scribeClient,
        ICalendarInviteBuilder invites,
        IPlatformSettings platform,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        ILogger<MeetingCancelledIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var meetingTitle = MeetingNotifications.Title(evt.MeetingTitle);
            await MeetingNotifications.PublishInAppAsync(
                dispatcher,
                evt.ParticipantUserIds.Concat(evt.InviteeUserIds),
                exclude: evt.CancelledByUserId,
                evt.TenantId,
                evt.MeetingId,
                evt.EventId,
                NotificationKinds.CommunicationMeetingCancelled,
                EventKey,
                evt.OccurredOn,
                $"\"{meetingTitle}\" was cancelled",
                string.IsNullOrWhiteSpace(evt.Reason) ? null : evt.Reason,
                correlation.CorrelationId,
                context: null,
                ct,
                new Dictionary<string, string> { ["meetingTitle"] = meetingTitle }
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
                        ["host_name"] = evt.HostDisplayName,
                        ["reason"] = string.IsNullOrWhiteSpace(evt.Reason) ? null : evt.Reason,
                    },
                    ScribeRenderAudience.Client,
                    locale,
                    ct
                )
            ).EnsureRendered(EventKey);

            foreach (var email in emails)
            {
                var invite = evt.ScheduledForUtc is { } startUtc
                    ? invites.Build(
                        MeetingNotifications.Invite(
                            CalendarInviteMethod.Cancel,
                            evt.MeetingId,
                            evt.MeetingTitle,
                            startUtc,
                            evt.DurationMinutes,
                            // ≥ cualquier reprogramación anterior: la cancelación siempre es posterior.
                            sequence: MeetingNotifications.SequenceAt(evt.CancelledAtUtc),
                            stampUtc: evt.CancelledAtUtc,
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
                        NotificationKinds.CommunicationMeetingCancelled,
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
                "Meeting {MeetingId} cancellation emailed to {Count} external invitee(s).",
                evt.MeetingId,
                emails.Count
            );
        }
    }
}
