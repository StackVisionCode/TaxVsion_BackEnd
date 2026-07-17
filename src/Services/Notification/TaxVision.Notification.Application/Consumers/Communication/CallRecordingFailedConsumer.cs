using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only — ver docblock de <see cref="MeetingRecordingReadyConsumer"/> sobre el
/// recipient simbólico, aquí <c>call:{CallId}</c>.
/// </summary>
public static class CallRecordingFailedConsumer
{
    public static async Task Handle(
        CallRecordingFailedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"call:{evt.CallId:N}",
                $"Grabación de llamada falló: {evt.Reason}",
                "communication.call.recording_failed",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
