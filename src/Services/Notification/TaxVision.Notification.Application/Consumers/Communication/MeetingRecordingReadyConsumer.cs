using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only, mismo criterio que <see cref="MeetingInvitationCreatedConsumer"/>: deja
/// constancia in-app de que una grabación de meeting quedó lista. Fase 1B — Communication ya
/// publica <c>HostUserId</c>, así que el recipient es el host real (<c>user:{HostUserId}</c>,
/// mismo formato que el resto de consumers de esta capa) en vez del placeholder simbólico
/// <c>meeting:{MeetingId}</c> de antes. Sigue sin enviar email ni push — solo registro in-app.
/// </summary>
public static class MeetingRecordingReadyConsumer
{
    public static async Task Handle(
        MeetingRecordingReadyIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var parameters = new Dictionary<string, string>
            {
                ["durationSeconds"] = evt.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture),
                ["participantCount"] = evt.ParticipantCount.ToString(CultureInfo.InvariantCulture),
            };
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CommunicationMeetingRecordingReady,
                    evt.TenantId,
                    new NotificationTarget(evt.HostUserId),
                    evt.EventId,
                    "communication.meeting.recording_ready.v1",
                    evt.ReadyAtUtc,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: parameters,
                        ResourceId: evt.RecordingFileId.ToString(),
                        ResourceContext: new Dictionary<string, string> { ["meetingId"] = evt.MeetingId.ToString() }
                    )
                ),
                ct
            );
        }
    }
}
