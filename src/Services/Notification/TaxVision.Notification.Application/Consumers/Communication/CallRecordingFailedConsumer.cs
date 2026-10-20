using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
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
            foreach (var participant in new[] { evt.CallerUserId, evt.CalleeUserId })
            {
                await dispatcher.RecordInAppAsync(
                    evt.TenantId,
                    $"user:{participant:N}",
                    subject,
                    NotificationCategory.Collaboration,
                    "communication.call.recording_failed",
                    evt.EventId,
                    correlation.CorrelationId,
                    recipientUserId: participant,
                    inApp: new InAppNotificationRequest(
                        NotificationKinds.CommunicationCallRecordingFailed,
                        participant,
                        evt.EventId,
                        "communication.call.recording_failed.v1",
                        evt.FailedAtUtc,
                        subject,
                        ResourceContext: new Dictionary<string, string> { ["callId"] = evt.CallId.ToString() },
                        Params: new Dictionary<string, string> { ["reason"] = evt.Reason }
                    ),
                    ct: ct
                );
            }
        }
    }
}
