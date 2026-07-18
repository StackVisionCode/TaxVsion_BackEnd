using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers;

// ---------------------------------------------------------------------------
// Fase 8 (Scribe): el contenido de los 6 emails de esta clase ya no se arma
// localmente (antes: static class EmailTemplates) — se renderiza en Scribe vía
// IScribeRenderClient.RenderAsync(eventKey, ...) y el resultado (Subject/Html/Text)
// se envía tal cual por IEmailDispatchGateway.QueueEmailAsync, como ya hacía
// Notifications Fase 3. SecurityAlertConsumer es la excepción: es notificación
// in-app (nunca pasó por el gateway de email), así que no llama a Scribe.
//
// Hardening Fase 7: cada llamada a RenderAsync termina en .EnsureRendered(eventKey)
// en vez de un "if (render.IsFailure) return;" manual — una falla de Scribe ahora
// lanza ScribeRenderFailedException para que la política global de retry+cooldown
// de Wolverine (Program.cs) reintente el mensaje, en vez de descartar el email
// (bienvenida, reset de password, invitación) sin dejar rastro.
// ---------------------------------------------------------------------------

public static class InvitationCreatedConsumer
{
    public static async Task Handle(
        InvitationCreatedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var office = string.IsNullOrWhiteSpace(evt.TenantName) ? portal.Value.ProductName : evt.TenantName!;
            var inviteLink =
                $"{portal.Value.BaseUrl.TrimEnd('/')}/accept-invitation?token={Uri.EscapeDataString(evt.RawToken)}";

            var render = (
                await scribeClient.RenderAsync(
                    "auth.invitation_created.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["office"] = office,
                        ["inviter"] = evt.InviterName ?? "El administrador",
                        ["invite_link"] = inviteLink,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["is_resend"] = evt.IsResend,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("auth.invitation_created.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "auth.invitation",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}

public static class PasswordResetRequestedConsumer
{
    public static async Task Handle(
        PasswordResetRequestedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var resetLink =
                $"{portal.Value.BaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(evt.RawToken)}";

            var render = (
                await scribeClient.RenderAsync(
                    "auth.password_reset_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["reset_link"] = resetLink,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("auth.password_reset_requested.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "auth.password_reset",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}

public static class TenantRecoveryRequestedConsumer
{
    public static async Task Handle(
        TenantRecoveryRequestedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var offices = evt
                .Matches.Select(match => new Dictionary<string, object?>
                {
                    ["name"] = match.TenantName,
                    ["url"] = $"https://{match.Host}",
                })
                .ToList();

            var render = (
                await scribeClient.RenderAsync(
                    "auth.tenant_recovery_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["offices"] = offices,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("auth.tenant_recovery_requested.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "auth.tenant_recovery",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}

public static class MfaChallengeRequestedConsumer
{
    public static async Task Handle(
        MfaChallengeRequestedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (string.Equals(evt.Channel, "Sms", StringComparison.OrdinalIgnoreCase))
            {
                var text =
                    $"{portal.Value.ProductName}: tu código es {evt.Code}. "
                    + "Expira en pocos minutos. No lo compartas.";
                // SMS sigue via NotificationDispatcher — Scribe/el gateway son exclusivos de email.
                await dispatcher.SendSmsAsync(
                    evt.TenantId,
                    evt.Destination,
                    text,
                    "auth.otp_code",
                    evt.EventId,
                    correlation.CorrelationId,
                    ct
                );
                return;
            }

            var reason = evt.Purpose switch
            {
                "login" => "iniciar sesión",
                "email_change" => "confirmar tu nuevo email",
                "phone_verification" => "verificar tu teléfono",
                _ => "continuar",
            };

            var render = (
                await scribeClient.RenderAsync(
                    "auth.mfa_otp_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["code"] = evt.Code,
                        ["reason"] = reason,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("auth.mfa_otp_requested.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Destination,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "auth.otp_code",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}

public static class EmailChangeRequestedConsumer
{
    public static async Task Handle(
        EmailChangeRequestedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var confirmLink =
                $"{portal.Value.BaseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(evt.RawToken)}";

            // Nota Fase 7: antes, si este primer render fallaba, el bloque se saltaba en silencio y
            // el consumer seguía directo a la alerta de seguridad — el email de confirmación se
            // perdía sin dejar rastro. Ahora EnsureRendered lanza de inmediato, así que Wolverine
            // reintenta el evento completo (ambos emails) en vez de completar con uno solo enviado.
            var confirmRender = (
                await scribeClient.RenderAsync(
                    "auth.email_change_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["confirm_link"] = confirmLink,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("auth.email_change_requested.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.NewEmail,
                    Subject: confirmRender.Subject,
                    HtmlBody: confirmRender.Html,
                    TextBody: confirmRender.Text ?? string.Empty,
                    TemplateKey: "auth.email_change",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: confirmRender.InlineAssets
                ),
                ct
            );

            // Comportamiento preservado tal cual del builder original: el alertType pasado aquí
            // ("email_change_requested") nunca coincidía con los casos reales del switch de
            // SecurityAlert, así que siempre caía al texto genérico — no es un bug nuevo de esta
            // migración, se mantiene la misma descripción por fidelidad de comportamiento.
            var warningRender = (
                await scribeClient.RenderAsync(
                    "auth.email_change_security_alert.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["description"] = "se registró actividad de seguridad en tu cuenta.",
                        ["ip_address"] = null,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("auth.email_change_security_alert.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.CurrentEmail,
                    Subject: warningRender.Subject,
                    HtmlBody: warningRender.Html,
                    TextBody: warningRender.Text ?? string.Empty,
                    TemplateKey: "auth.security_alert",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: warningRender.InlineAssets
                ),
                ct
            );
        }
    }
}

// Notificación in-app — nunca pasó por el gateway de email ni por Scribe (Scribe solo renderiza
// HTML de correo); el subject es texto plano fijo, igual que el builder original.
public static class SecurityAlertConsumer
{
    public static async Task Handle(
        SecurityAlertIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // El evento no transporta el email del usuario (dato de Auth); se
            // registra como notificación in-app dirigida al usuario. Cuando exista
            // la proyección de usuarios en Notification se enviará también por correo.
            var subject = $"Alerta de seguridad en tu cuenta de {portal.Value.ProductName}";
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.UserId:N}",
                subject,
                "auth.security_alert",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}

// Fase 9: primer consumer real de auth.welcome — el evento vivía en el namespace local de Auth
// y se movió a BuildingBlocks.Messaging.AuthIntegrationEvents para que Notification pudiera
// referenciarlo (ver memoria project_scribe_fase8_migration.md).
public static class UserRegisteredConsumer
{
    public static async Task Handle(
        UserRegisteredIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var render = (
                await scribeClient.RenderAsync(
                    "auth.user_registered.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["name"] = ResolveName(evt),
                        ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("auth.user_registered.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "auth.welcome",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }

    private static string ResolveName(UserRegisteredIntegrationEvent evt)
    {
        var fullName = $"{evt.Name} {evt.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? evt.Email.Split('@')[0] : fullName;
    }
}

internal static class Correlation
{
    public static string From(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
