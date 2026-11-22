using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

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
            var parameters = new Dictionary<string, string>
            {
                ["durationSeconds"] = evt.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture),
            };
            foreach (var participant in new[] { evt.CallerUserId, evt.CalleeUserId })
            {
                await dispatcher.DispatchAsync(
                    new NotificationIntent(
                        NotificationKinds.CommunicationCallRecordingReady,
                        evt.TenantId,
                        new NotificationTarget(participant),
                        evt.EventId,
                        "communication.call.recording_ready.v1",
                        evt.ReadyAtUtc,
                        correlation.CorrelationId,
                        InApp: new InAppContent(
                            Params: parameters,
                            ResourceId: evt.RecordingFileId.ToString(),
                            ResourceContext: new Dictionary<string, string> { ["callId"] = evt.CallId.ToString() }
                        )
                    ),
                    ct
                );
            }
        }
    }
}
