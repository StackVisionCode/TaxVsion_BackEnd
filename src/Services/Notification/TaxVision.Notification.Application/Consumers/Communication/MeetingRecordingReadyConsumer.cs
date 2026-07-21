using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub log-only, mismo criterio que <see cref="MeetingInvitationCreatedConsumer"/>: deja
/// constancia in-app de que una grabación de meeting quedó lista. Fase 1B — Communication ya
/// publica <c>HostUserId</c>, así que el recipient es el host real (<c>user:{HostUserId}</c>,
/// mismo formato que el resto de consumers de esta capa) en vez del placeholder simbólico
/// <c>meeting:{MeetingId}</c> de antes. Sigue sin enviar email ni push — solo registro in-app.
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
                $"user:{evt.HostUserId:N}",
                $"Grabación de meeting lista ({evt.DurationSeconds:F0}s, {evt.ParticipantCount} participantes)",
                NotificationCategory.Collaboration,
                "communication.meeting.recording_ready",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.HostUserId,
                ct: ct
            );
        }
    }
}
