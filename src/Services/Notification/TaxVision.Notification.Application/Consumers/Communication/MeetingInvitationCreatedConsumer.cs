using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Stub de Fase Backend 5 (Communication) — registra la invitación como notificación
/// in-app (log/audit) sin enviar email real. El envío real de correo llega en una fase
/// posterior de Notification; por ahora esto deja constancia de <c>joinUrl</c> +
/// <c>expiresAtUtc</c> + destinatario para poder probar el flujo manualmente
/// (ver <see cref="NotificationDispatcher.RecordInAppAsync"/>).
/// </summary>
public static class MeetingInvitationCreatedConsumer
{
    public static async Task Handle(
        MeetingInvitationCreatedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipient = evt.InviteeUserId is { } userId ? $"user:{userId:N}" : evt.InviteeEmail ?? "unknown";

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                recipient,
                $"Invitación a meeting ({evt.InviteeKind}) — expira {evt.ExpiresAtUtc:u}",
                NotificationCategory.Collaboration,
                "communication.meeting.invitation_created",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.InviteeUserId,
                ct: ct
            );
        }
    }
}
