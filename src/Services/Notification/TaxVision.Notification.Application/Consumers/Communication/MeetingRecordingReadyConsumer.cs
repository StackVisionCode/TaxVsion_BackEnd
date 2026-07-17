using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only, mismo criterio que <see cref="MeetingInvitationCreatedConsumer"/>: deja
/// constancia in-app de que una grabación de meeting quedó lista. A diferencia de la
/// invitación, este evento no trae un destinatario (ni userId ni email) — Communication no
/// resuelve quién debe verlo antes de publicar — así que se registra contra un recipient
/// simbólico <c>meeting:{MeetingId}</c> en vez de un usuario real. No envía email ni push;
/// eso requeriría que Communication resuelva y publique el host/organizador del meeting.
/// </summary>
public static class MeetingRecordingReadyConsumer
{
    public static async Task Handle(
        MeetingRecordingReadyIntegrationEvent evt,
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
                $"Grabación de meeting lista ({evt.DurationSeconds:F0}s, {evt.ParticipantCount} participantes)",
                "communication.meeting.recording_ready",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
