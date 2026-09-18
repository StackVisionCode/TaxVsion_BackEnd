using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
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
/// </summary>
public static class MeetingInvitationCreatedConsumer
{
    public static async Task Handle(
        MeetingInvitationCreatedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        ILogger<MeetingInvitationCreatedLog> logger,
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

            if (string.IsNullOrWhiteSpace(evt.InviteeEmail))
            {
                logger.LogInformation(
                    "Meeting invitation {InvitationId} has no email; recorded in-app only.",
                    evt.InvitationId
                );
                return;
            }

            var render = (
                await scribeClient.RenderAsync(
                    "communication.meeting.invitation_created.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["invitee_name"] = string.IsNullOrWhiteSpace(evt.InviteeName) ? "there" : evt.InviteeName!,
                        ["join_link"] = evt.JoinUrl,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("communication.meeting.invitation_created.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.InviteeEmail!,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "communication.meeting.invitation",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}

/// <summary>Ancla de categoría para el <see cref="ILogger{T}"/> del consumer (evita un logger genérico).</summary>
public sealed class MeetingInvitationCreatedLog { }
