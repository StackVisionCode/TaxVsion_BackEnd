using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only, mismo criterio que <see cref="MeetingRecordingReadyConsumer"/> pero para
/// llamadas 1:1 — recipient simbólico <c>call:{CallId}</c>.
/// </summary>
public static class CallRecordingReadyConsumer
{
    public static async Task Handle(
        CallRecordingReadyIntegrationEvent evt,
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
                $"Grabación de llamada lista ({evt.DurationSeconds:F0}s)",
                "communication.call.recording_ready",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
