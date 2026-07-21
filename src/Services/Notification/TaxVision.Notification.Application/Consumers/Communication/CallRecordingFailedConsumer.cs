using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only — ver docblock de <see cref="CallRecordingReadyConsumer"/> sobre el
/// recipient real por participante (Fase 1B).
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
            var subject = $"Grabación de llamada falló: {evt.Reason}";
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.CallerUserId:N}",
                subject,
                NotificationCategory.Collaboration,
                "communication.call.recording_failed",
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
                "communication.call.recording_failed",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.CalleeUserId,
                ct: ct
            );
        }
    }
}
