using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

// ---------------------------------------------------------------------------
// Fase 8 (Scribe): el contenido de los 6 emails de esta clase ya no se arma
// localmente (antes: static class EmailTemplates) — se renderiza en Scribe vía
// IScribeRenderClient.RenderAsync(eventKey, ...) y el resultado (Subject/Html/Text)
// se envía tal cual por IEmailDispatchGateway.QueueEmailAsync, como ya hacía
// Notifications Fase 3. SecurityAlertConsumer también: correo al afectado con la
// plantilla de alerta de Scribe, además de la campanita.
//
// Hardening Fase 7: cada llamada a RenderAsync termina en .EnsureRendered(eventKey)
// en vez de un "if (render.IsFailure) return;" manual — una falla de Scribe ahora
// lanza ScribeRenderFailedException para que la política global de retry+cooldown
// de Wolverine (Program.cs) reintente el mensaje, en vez de descartar el email
// (bienvenida, reset de password, invitación) sin dejar rastro.
//
// Invitación, reset y confirmación de cambio de correo llevan un token en el enlace: van como
// Confidential (directo a la cola de Postmaster, nunca al fanout taxvision-events).
// ---------------------------------------------------------------------------

public static class InvitationCreatedConsumer
{
    public static async Task Handle(
        InvitationCreatedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        IPlatformSettings platform,
        AuthAccountTokenReader tokens,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var token = tokens.Read(
                evt.ProtectedToken,
                evt.RawToken,
                AuthTokenPayloadSchemas.Invitation,
                AuthTokenPayloadSchemas.InvitationBinding(evt.TenantId, evt.InvitationId),
                evt.EventId
            );
            if (token is null)
                return;

            var office = string.IsNullOrWhiteSpace(evt.TenantName) ? platform.Current.BrandName : evt.TenantName!;
            var tenantPortalUrl = string.IsNullOrWhiteSpace(evt.TenantSubdomain)
                ? portal.Value.BaseUrl.TrimEnd('/')
                : $"https://{platform.Current.TenantHost(evt.TenantSubdomain.Trim().ToLowerInvariant())}";
            var inviteLink = $"{tenantPortalUrl}/accept-invitation?token={Uri.EscapeDataString(token)}";

            // Quien aún no tiene cuenta no tiene idioma propio, y el evento no trae uno: inglés.
            var render = (
                await scribeClient.RenderForRecipientAsync(
                    "auth.invitation_created.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["office"] = office,
                        ["inviter"] = string.IsNullOrWhiteSpace(evt.InviterName) ? office : evt.InviterName,
                        ["account_kind"] = AuthAccountCopy.Kind(evt.ActorType),
                        ["invite_link"] = inviteLink,
                        ["expires_at"] = ScribeDates.Instant(evt.ExpiresAtUtc),
                        ["is_resend"] = evt.IsResend,
                    },
                    AuthAccountCopy.Audience(evt.ActorType),
                    locales.FromEventLanguage(null),
                    ct
                )
            ).EnsureRendered("auth.invitation_created.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Email,
                    templateKey: "auth.invitation",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
                ) with
                {
                    Confidential = true,
                },
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
        IPlatformSettings platform,
        ITenantHostResolver hostResolver,
        AuthAccountTokenReader tokens,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // Link al subdominio de la oficina (no a un base fijo): el reset lo resuelve por Host.
            // El destino depende del actor de ESTA oficina: cliente → portal, staff → CRM. Un email
            // multi-oficina recibe un correo por oficina, cada uno con su link al destino correcto.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var token = tokens.Read(
                evt.ProtectedToken,
                evt.RawToken,
                AuthTokenPayloadSchemas.PasswordReset,
                AuthTokenPayloadSchemas.PasswordResetBinding(evt.TenantId, evt.UserId),
                evt.EventId
            );
            if (token is null)
                return;

            var resetToken = Uri.EscapeDataString(token);
            var resetLink = AuthAccountCopy.IsPortal(evt.ActorType)
                ? $"{TenantEmailLinks.ClientBase(tenantHost, portal.Value)}/client/auth/reset-password/new?token={resetToken}"
                : $"{TenantEmailLinks.StaffBase(tenantHost, portal.Value)}/reset-password?token={resetToken}";

            var render = (
                await scribeClient.RenderForRecipientAsync(
                    "auth.password_reset_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["reset_link"] = resetLink,
                        ["expires_at"] = ScribeDates.Instant(evt.ExpiresAtUtc),
                        ["account_kind"] = AuthAccountCopy.Kind(evt.ActorType),
                        ["office"] = string.IsNullOrWhiteSpace(evt.TenantName)
                            ? platform.Current.BrandName
                            : evt.TenantName,
                    },
                    AuthAccountCopy.Audience(evt.ActorType),
                    await locales.ForUserAsync(evt.TenantId, evt.UserId, ct),
                    ct
                )
            ).EnsureRendered("auth.password_reset_requested.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Email,
                    templateKey: "auth.password_reset",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
                ) with
                {
                    Confidential = true,
                },
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
        IRecipientLocale locales,
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

            // Un correo que puede ser de varias oficinas y sin usuario: inglés.
            var render = (
                await scribeClient.RenderForRecipientAsync(
                    "auth.tenant_recovery_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?> { ["offices"] = offices },
                    ScribeRenderAudience.Staff,
                    locales.FromEventLanguage(null),
                    ct
                )
            ).EnsureRendered("auth.tenant_recovery_requested.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Email,
                    templateKey: "auth.tenant_recovery",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
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
        ICustomerPortalAccountProjectionRepository portalAccounts,
        NotificationDispatcher dispatcher,
        IRecipientLocale locales,
        IOptions<PortalOptions> portal,
        IPlatformSettings platform,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var locale = await locales.ForUserAsync(evt.TenantId, evt.UserId, ct);
            if (string.Equals(evt.Channel, "Sms", StringComparison.OrdinalIgnoreCase))
            {
                var text = SmsText.For(
                    SmsText.MfaCode,
                    locale,
                    new Dictionary<string, string> { ["brand"] = platform.Current.BrandName, ["code"] = evt.Code }
                );
                // SMS sigue via NotificationDispatcher — Scribe/el gateway son exclusivos de email.
                await dispatcher.SendSmsAsync(
                    evt.TenantId,
                    evt.Destination,
                    text,
                    NotificationCategory.AccountSecurity,
                    "auth.otp_code",
                    evt.EventId,
                    correlation.CorrelationId,
                    ct: ct
                );
                return;
            }

            // La plantilla escribe el motivo en el idioma de quien lee; aquí solo viaja el código.
            var reasonCode = evt.Purpose switch
            {
                "login" or "email_change" or "phone_verification" => evt.Purpose,
                _ => "continue",
            };

            // El evento no trae el tipo de actor: el usuario es cliente si es el de una cuenta de portal.
            var audience = await portalAccounts.GetByUserAsync(evt.TenantId, evt.UserId, ct) is not null
                ? ScribeRenderAudience.Client
                : ScribeRenderAudience.Staff;
            var render = (
                await scribeClient.RenderForRecipientAsync(
                    "auth.mfa_otp_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?> { ["code"] = evt.Code, ["reason_code"] = reasonCode },
                    audience,
                    locale,
                    ct
                )
            ).EnsureRendered("auth.mfa_otp_requested.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Destination,
                    templateKey: "auth.otp_code",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
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
        IPlatformSettings platform,
        ITenantHostResolver hostResolver,
        AuthAccountTokenReader tokens,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // Link al subdominio de la oficina: el cliente confirma en su portal y el staff en el CRM.
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var token = tokens.Read(
                evt.ProtectedToken,
                evt.RawToken,
                AuthTokenPayloadSchemas.EmailChange,
                AuthTokenPayloadSchemas.EmailChangeBinding(evt.TenantId, evt.UserId),
                evt.EventId
            );
            if (token is null)
                return;

            var confirmToken = Uri.EscapeDataString(token);
            var confirmLink = AuthAccountCopy.IsPortal(evt.ActorType)
                ? $"{TenantEmailLinks.ClientBase(tenantHost, portal.Value)}/client/auth/confirm-email?token={confirmToken}"
                : $"{TenantEmailLinks.StaffBase(tenantHost, portal.Value)}/confirm-email?token={confirmToken}";
            var office = string.IsNullOrWhiteSpace(evt.TenantName) ? platform.Current.BrandName : evt.TenantName;
            var locale = await locales.ForUserAsync(evt.TenantId, evt.UserId, ct);

            // Nota Fase 7: antes, si este primer render fallaba, el bloque se saltaba en silencio y
            // el consumer seguía directo a la alerta de seguridad — el email de confirmación se
            // perdía sin dejar rastro. Ahora EnsureRendered lanza de inmediato, así que Wolverine
            // reintenta el evento completo (ambos emails) en vez de completar con uno solo enviado.
            var confirmRender = (
                await scribeClient.RenderForRecipientAsync(
                    "auth.email_change_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["confirm_link"] = confirmLink,
                        ["expires_at"] = ScribeDates.Instant(evt.ExpiresAtUtc),
                        ["account_kind"] = AuthAccountCopy.Kind(evt.ActorType),
                        ["office"] = office,
                    },
                    AuthAccountCopy.Audience(evt.ActorType),
                    locale,
                    ct
                )
            ).EnsureRendered("auth.email_change_requested.v1");

            await gateway.QueueEmailAsync(
                confirmRender.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.NewEmail,
                    templateKey: "auth.email_change",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
                ) with
                {
                    Confidential = true,
                },
                ct
            );

            // Aviso a la dirección anterior: dice qué cuenta pidió el cambio y hacia qué email.
            var warningRender = (
                await scribeClient.RenderForRecipientAsync(
                    "auth.email_change_security_alert.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["description"] = EmailText.For(
                            AuthAccountCopy.IsPortal(evt.ActorType)
                                ? "auth.email_change_requested.client"
                                : "auth.email_change_requested.staff",
                            locale,
                            new Dictionary<string, string> { ["office"] = office!, ["newEmail"] = evt.NewEmail }
                        ),
                        ["ip_address"] = null,
                    },
                    AuthAccountCopy.Audience(evt.ActorType),
                    locale,
                    ct
                )
            ).EnsureRendered("auth.email_change_security_alert.v1");

            await gateway.QueueEmailAsync(
                warningRender.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.CurrentEmail,
                    templateKey: "auth.security_alert",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
                ),
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
        ITenantHostResolver hostResolver,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var tenantHost = await hostResolver.ResolveHostAsync(evt.TenantId, ct);
            var render = (
                await scribeClient.RenderForRecipientAsync(
                    "auth.user_registered.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["name"] = ResolveName(evt),
                        // Un cliente (CustomerPortal) entra por el portal (/portal); el staff, por la
                        // raíz (CRM). Mandar a todos a la raíz llevaba al cliente al CRM del staff.
                        ["portal_link"] =
                            evt.ActorType == "CustomerPortal"
                                ? TenantEmailLinks.ClientBase(tenantHost, portal.Value)
                                : TenantEmailLinks.StaffBase(tenantHost, portal.Value),
                    },
                    AuthAccountCopy.Audience(evt.ActorType),
                    await locales.ForUserAsync(evt.TenantId, evt.UserId, ct),
                    ct
                )
            ).EnsureRendered("auth.user_registered.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    tenantId: evt.TenantId,
                    to: evt.Email,
                    templateKey: "auth.welcome",
                    relatedEventId: evt.EventId,
                    correlationId: correlation.CorrelationId
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
