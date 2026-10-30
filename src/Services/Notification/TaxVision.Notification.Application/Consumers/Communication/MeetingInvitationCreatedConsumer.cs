using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Invitación a un meeting (publicada por Communication, Node.js): registra la notificación in-app
/// (inbox del invitado) Y envía el correo real. El correo se renderiza en Scribe
/// (<c>communication.meeting.invitation_created.v1</c>) y se despacha por Postmaster, mismo patrón que
/// <see cref="TaxVision.Notification.Application.Consumers.InvitationCreatedConsumer"/>.
///
/// <para>
/// El <c>JoinUrl</c> ya viene armado por Communication con el subdominio correcto del tenant y la ruta
/// según el tipo de invitado (portal del cliente vs CRM), así que acá NO se resuelve host. Un invitado
/// sin email (no debería pasar para Employee/Customer/External, todos traen email) solo recibe el
/// in-app. Nota: <c>evt.TenantId</c> (heredado) se deserializa del evento Node — verificar en vivo que
/// no llegue <c>Guid.Empty</c> (ver docblock de <see cref="MeetingInvitationCreatedIntegrationEvent"/>).
/// </para>
///
/// <para>
/// F6: el correo lleva título, hora local con la zona de la oficina, duración, host y código, y si la
/// reunión tiene fecha, un <c>invite.ics</c> (<c>METHOD:REQUEST</c>). Una reunión en curso
/// (<c>MeetingStatus = Live</c>) no tiene fecha que poner en el calendario: el correo dice "en curso" y
/// no adjunta nada. Un invitado con cuenta pasa por su preferencia de Calendar; el externo recibe siempre.
/// </para>
/// </summary>
public static class MeetingInvitationCreatedConsumer
{
    private const string EventKey = "communication.meeting.invitation_created.v1";
    private const string TemplateKey = "communication.meeting.invitation";

    public static async Task Handle(
        MeetingInvitationCreatedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IUserNotificationPreferenceRepository preferences,
        ICalendarInviteBuilder invites,
        IPlatformSettings platform,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        ILogger<MeetingInvitationCreatedLog> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var recipient = evt.InviteeUserId is { } userId ? $"user:{userId:N}" : evt.InviteeEmail ?? "unknown";
            var meetingTitle = MeetingNotifications.Title(evt.MeetingTitle);
            var inProgress = string.Equals(evt.MeetingStatus, "Live", StringComparison.OrdinalIgnoreCase);
            var title = inProgress
                ? $"You're invited to join \"{meetingTitle}\" now"
                : $"You're invited to \"{meetingTitle}\"";
            var body = string.IsNullOrWhiteSpace(evt.InvitedByDisplayName)
                ? null
                : $"{evt.InvitedByDisplayName} invited you.";

            // Solo el invitado con cuenta llega a la campana; los externos sin UserId reciben solo el email.
            var inApp = evt.InviteeUserId is { } inviteeUserId
                ? new InAppNotificationRequest(
                    NotificationKinds.CommunicationMeetingInvitationCreated,
                    inviteeUserId,
                    evt.EventId,
                    EventKey,
                    evt.OccurredOn,
                    title,
                    FallbackBody: body,
                    ActorUserId: evt.InvitedByUserId,
                    ActorDisplayName: evt.InvitedByDisplayName,
                    ResourceId: evt.MeetingId.ToString(),
                    ResourceContext: InAppContext(evt, inProgress)
                )
                : null;

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                recipient,
                title,
                NotificationCategory.Calendar,
                NotificationKinds.CommunicationMeetingInvitationCreated,
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.InviteeUserId,
                inApp: inApp,
                ct: ct
            );

            if (string.IsNullOrWhiteSpace(evt.InviteeEmail))
            {
                logger.LogInformation(
                    "Meeting invitation {InvitationId} has no email; recorded in-app only.",
                    evt.InvitationId
                );
                return;
            }

            if (
                !await MeetingNotifications.EmailAllowedAsync(
                    preferences,
                    evt.TenantId,
                    evt.InviteeUserId,
                    logger,
                    TemplateKey,
                    ct
                )
            )
                return;

            var render = (
                await scribeClient.RenderAsync(
                    EventKey,
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["invitee_name"] = string.IsNullOrWhiteSpace(evt.InviteeName) ? "there" : evt.InviteeName!,
                        ["join_link"] = evt.JoinUrl,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["meeting_title"] = meetingTitle,
                        ["start_local"] = MeetingNotifications.LocalTime(evt.ScheduledForUtc, evt.TimeZone),
                        ["time_zone"] = MeetingNotifications.TimeZoneLabel(evt.TimeZone),
                        ["duration_minutes"] = evt.DurationMinutes,
                        ["host_name"] = evt.HostDisplayName,
                        ["meeting_code"] = evt.ShortCode,
                        ["in_progress"] = inProgress,
                    },
                    ct
                )
            ).EnsureRendered(EventKey);

            // Sin fecha no hay nada que poner en el calendario (reunión en curso o sin agendar).
            var invite = evt.ScheduledForUtc is { } startUtc
                ? invites.Build(
                    MeetingNotifications.Invite(
                        CalendarInviteMethod.Request,
                        evt.MeetingId,
                        evt.MeetingTitle,
                        startUtc,
                        evt.DurationMinutes,
                        sequence: 0,
                        stampUtc: evt.OccurredOn,
                        url: evt.JoinUrl,
                        meetingCode: evt.ShortCode,
                        hostDisplayName: evt.HostDisplayName,
                        platform,
                        attendeeEmail: evt.InviteeEmail!,
                        attendeeName: evt.InviteeName
                    )
                )
                : null;

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.InviteeEmail!,
                    templateKey: TemplateKey,
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
                ) with
                {
                    InlineAttachments = invite is null ? null : [invite],
                },
                ct
            );
        }
    }

    private static Dictionary<string, string> InAppContext(
        MeetingInvitationCreatedIntegrationEvent evt,
        bool inProgress
    )
    {
        var context = new Dictionary<string, string>
        {
            ["invitationId"] = evt.InvitationId.ToString(),
            ["status"] = inProgress ? "Live" : "Scheduled",
        };
        if (!string.IsNullOrWhiteSpace(evt.ShortCode))
            context["shortCode"] = evt.ShortCode;
        if (evt.ScheduledForUtc is { } scheduledFor)
            context["scheduledForUtc"] = scheduledFor.ToString("O");
        return context;
    }
}

/// <summary>Ancla de categoría para el <see cref="ILogger{T}"/> del consumer (evita un logger genérico).</summary>
public sealed class MeetingInvitationCreatedLog { }
