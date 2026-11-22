using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only — ver docblock de <see cref="MeetingRecordingReadyConsumer"/> sobre el
/// recipient real, aquí <c>user:{HostUserId}</c> (Fase 1B).
/// </summary>
public static class MeetingRecordingFailedConsumer
{
    public static async Task Handle(
        MeetingRecordingFailedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var parameters = new Dictionary<string, string> { ["reason"] = evt.Reason };
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CommunicationMeetingRecordingFailed,
                    evt.TenantId,
                    new NotificationTarget(evt.HostUserId),
                    evt.EventId,
                    "communication.meeting.recording_failed.v1",
                    evt.FailedAtUtc,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: parameters,
                        ResourceContext: new Dictionary<string, string> { ["meetingId"] = evt.MeetingId.ToString() }
                    )
                ),
                ct
            );
        }
    }
}
