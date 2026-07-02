using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers;

// ---------------------------------------------------------------------------
// Invitaciones (empleados, admins y portal cliente)
// ---------------------------------------------------------------------------

public static class InvitationCreatedConsumer
{
    public static async Task Handle(
        InvitationCreatedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var email = EmailTemplates.Invitation(
                portal.Value,
                evt.TenantName,
                evt.InviterName,
                evt.RawToken,
                evt.ExpiresAtUtc,
                evt.IsResend);

            await dispatcher.SendEmailAsync(
                evt.TenantId, evt.Email, email, EmailTemplates.InvitationKey,
                evt.EventId, correlation.CorrelationId, ct);
        }
    }
}

// ---------------------------------------------------------------------------
// Recuperación de contraseña
// ---------------------------------------------------------------------------

public static class PasswordResetRequestedConsumer
{
    public static async Task Handle(
        PasswordResetRequestedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var email = EmailTemplates.PasswordReset(portal.Value, evt.RawToken, evt.ExpiresAtUtc);
            await dispatcher.SendEmailAsync(
                evt.TenantId, evt.Email, email, EmailTemplates.PasswordResetKey,
                evt.EventId, correlation.CorrelationId, ct);
        }
    }
}

// ---------------------------------------------------------------------------
// OTP (login MFA, verificación de teléfono) por email o SMS
// ---------------------------------------------------------------------------

public static class MfaChallengeRequestedConsumer
{
    public static async Task Handle(
        MfaChallengeRequestedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (string.Equals(evt.Channel, "Sms", StringComparison.OrdinalIgnoreCase))
            {
                var text =
                    $"{portal.Value.ProductName}: tu código es {evt.Code}. " +
                    "Expira en pocos minutos. No lo compartas.";
                await dispatcher.SendSmsAsync(
                    evt.TenantId, evt.Destination, text, EmailTemplates.OtpCodeKey,
                    evt.EventId, correlation.CorrelationId, ct);
                return;
            }

            var email = EmailTemplates.OtpCode(portal.Value, evt.Code, evt.Purpose);
            await dispatcher.SendEmailAsync(
                evt.TenantId, evt.Destination, email, EmailTemplates.OtpCodeKey,
                evt.EventId, correlation.CorrelationId, ct);
        }
    }
}

// ---------------------------------------------------------------------------
// Cambio de email: enlace a la dirección nueva + aviso a la anterior
// ---------------------------------------------------------------------------

public static class EmailChangeRequestedConsumer
{
    public static async Task Handle(
        EmailChangeRequestedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var confirm = EmailTemplates.EmailChange(portal.Value, evt.RawToken, evt.ExpiresAtUtc);
            await dispatcher.SendEmailAsync(
                evt.TenantId, evt.NewEmail, confirm, EmailTemplates.EmailChangeKey,
                evt.EventId, correlation.CorrelationId, ct);

            var warning = EmailTemplates.SecurityAlert(portal.Value, "email_change_requested", null);
            await dispatcher.SendEmailAsync(
                evt.TenantId, evt.CurrentEmail, warning, EmailTemplates.SecurityAlertKey,
                evt.EventId, correlation.CorrelationId, ct);
        }
    }
}

// ---------------------------------------------------------------------------
// Alertas de seguridad (reuse detection, lockout, MFA off, password/email cambiados)
// ---------------------------------------------------------------------------

public static class SecurityAlertConsumer
{
    public static async Task Handle(
        SecurityAlertIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // El evento no transporta el email del usuario (dato de Auth); se
            // registra como notificación in-app dirigida al usuario. Cuando exista
            // la proyección de usuarios en Notification se enviará también por correo.
            var alert = EmailTemplates.SecurityAlert(portal.Value, evt.AlertType, evt.IpAddress);
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.UserId:N}",
                alert.Subject,
                EmailTemplates.SecurityAlertKey,
                evt.EventId,
                correlation.CorrelationId,
                ct);
        }
    }
}

internal static class Correlation
{
    public static string From(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
