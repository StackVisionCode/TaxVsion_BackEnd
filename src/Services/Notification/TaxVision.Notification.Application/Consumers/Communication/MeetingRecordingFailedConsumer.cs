using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

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
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.HostUserId:N}",
                $"Grabación de meeting falló: {evt.Reason}",
                NotificationCategory.Collaboration,
                "communication.meeting.recording_failed",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.HostUserId,
                ct: ct
            );
        }
    }
}
