using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

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
            var parameters = new Dictionary<string, string> { ["reason"] = evt.Reason };
            foreach (var participant in new[] { evt.CallerUserId, evt.CalleeUserId })
            {
                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CommunicationCallRecordingFailed,
                        evt.TenantId,
                        new NotificationTarget(participant),
                        evt.EventId,
                        "communication.call.recording_failed.v1",
                        evt.FailedAtUtc,
                        correlation.CorrelationId,
                        InApp: new InAppContent(
                            Params: parameters,
                            ResourceContext: new Dictionary<string, string> { ["callId"] = evt.CallId.ToString() }
                        )
                    ),
                    ct
                );
            }
        }
    }
}
