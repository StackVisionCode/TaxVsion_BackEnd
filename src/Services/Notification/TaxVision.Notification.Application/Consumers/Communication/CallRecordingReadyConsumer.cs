using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only, mismo criterio que <see cref="MeetingRecordingReadyConsumer"/> pero para
/// llamadas 1:1. Fase 1B — una Call no tiene dueño único (ver docblock de
/// <c>CallRecordingFailedIntegrationEvent</c>), así que se registra un recipient real por
/// cada participante (<c>user:{CallerUserId}</c> y <c>user:{CalleeUserId}</c>) en vez del
/// placeholder simbólico <c>call:{CallId}</c> de antes.
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
            var subject = $"Grabación de llamada lista ({evt.DurationSeconds:F0}s)";
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.CallerUserId:N}",
                subject,
                NotificationCategory.Collaboration,
                "communication.call.recording_ready",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.CallerUserId,
                ct: ct
            );
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.CalleeUserId:N}",
                subject,
                NotificationCategory.Collaboration,
                "communication.call.recording_ready",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.CalleeUserId,
                ct: ct
            );
        }
    }
}
