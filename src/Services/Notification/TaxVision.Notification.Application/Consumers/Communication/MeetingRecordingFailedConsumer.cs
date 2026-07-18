using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only — ver docblock de <see cref="MeetingRecordingReadyConsumer"/> sobre el
/// recipient simbólico <c>meeting:{MeetingId}</c>.
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
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"meeting:{evt.MeetingId:N}",
                $"Grabación de meeting falló: {evt.Reason}",
                "communication.meeting.recording_failed",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
