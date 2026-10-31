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
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ICalendarInviteBuilder invites,
        IPlatformSettings platform,
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
                        ["start_local"] = MeetingNotifications.LocalTime(evt.ScheduledForUtc, evt.TimeZone),
                        ["time_zone"] = MeetingNotifications.TimeZoneLabel(evt.TimeZone),
                        ["host_name"] = evt.HostDisplayName,
                        ["reason"] = string.IsNullOrWhiteSpace(evt.Reason) ? null : evt.Reason,
                    },
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

                await gateway.QueueEmailAsync(
                    render.ToDispatchRequest(
                        tenantId: evt.TenantId,
                        to: email,
                        templateKey: EventKey,
                        relatedEventId: evt.EventId,
                        correlationId: correlation.CorrelationId
                    ) with
                    {
                        InlineAttachments = invite is null ? null : [invite],
                    },
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
