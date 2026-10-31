using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// <c>communication.meeting.invitation_revoked.v1</c> — el host retiró una invitación. Solo in-app al
/// invitado con cuenta: a un externo no se le escribe (su enlace deja de funcionar y, si estaba dentro,
/// el socket ya le dijo que lo sacaron). Un correo diciendo "ya no estás invitado" no le sirve a nadie.
/// </summary>
public static class MeetingInvitationRevokedConsumer
{
    private const string EventKey = "communication.meeting.invitation_revoked.v1";

    public static async Task Handle(
        MeetingInvitationRevokedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        ILogger<MeetingInvitationRevokedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (evt.InviteeUserId is not { } userId || userId == Guid.Empty)
        {
            logger.LogInformation(
                "Meeting invitation {InvitationId} revoked for an external invitee; nothing to notify.",
                evt.InvitationId
            );
            return;
        }

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var meetingTitle = MeetingNotifications.Title(evt.MeetingTitle);
            await MeetingNotifications.PublishInAppAsync(
                dispatcher,
                [userId],
                exclude: null,
                evt.TenantId,
                evt.MeetingId,
                evt.EventId,
                NotificationKinds.CommunicationMeetingInvitationRevoked,
                EventKey,
                evt.OccurredOn,
                $"Your invitation to \"{meetingTitle}\" was revoked",
                evt.Ejected ? "You were removed from the meeting." : null,
                correlation.CorrelationId,
                new Dictionary<string, string>
                {
                    ["invitationId"] = evt.InvitationId.ToString(),
                    ["ejected"] = evt.Ejected ? "true" : "false",
                },
                ct
            );
        }
    }
}
