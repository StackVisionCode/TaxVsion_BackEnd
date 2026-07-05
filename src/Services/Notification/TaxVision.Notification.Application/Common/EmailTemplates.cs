using System.Net;

namespace TaxVision.Notification.Application.Common;

/// <summary>URLs públicas del frontend para construir enlaces en los correos.</summary>
public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>Base de la app privada, p. ej. https://app.taxvision.com</summary>
    public string BaseUrl { get; set; } = "http://localhost:4200";

    public string ProductName { get; set; } = "TaxVision";
}

public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Plantillas de correo del sistema. Deliberadamente simples (HTML inline);
/// el módulo Builder Template las sustituirá para contenidos por tenant.
/// Todos los valores de usuario se codifican en HTML para evitar inyección.
/// </summary>
public static class EmailTemplates
{
    public const string InvitationKey = "auth.invitation";
    public const string PasswordResetKey = "auth.password_reset";
    public const string OtpCodeKey = "auth.otp_code";
    public const string EmailChangeKey = "auth.email_change";
    public const string SecurityAlertKey = "auth.security_alert";
    public const string WelcomeKey = "auth.welcome";

    public static RenderedEmail Invitation(
        PortalOptions portal,
        string? tenantName,
        string? inviterName,
        string rawToken,
        DateTime expiresAtUtc,
        bool isResend
    )
    {
        var office = Encode(string.IsNullOrWhiteSpace(tenantName) ? portal.ProductName : tenantName!);
        var inviter = Encode(inviterName ?? "El administrador");
        var link = $"{portal.BaseUrl.TrimEnd('/')}/accept-invitation?token={Uri.EscapeDataString(rawToken)}";
        var subject = isResend
            ? $"Recordatorio: tu invitación a {office} en {portal.ProductName}"
            : $"Has sido invitado a {office} en {portal.ProductName}";
        var text =
            $"{inviter} te invitó a unirte a {office} en {portal.ProductName}.\n"
            + $"Activa tu cuenta: {link}\n"
            + $"El enlace expira el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.";
        var html = Layout(
            subject,
            $"""
            <p>{inviter} te invitó a unirte a <strong>{office}</strong> en {Encode(portal.ProductName)}.</p>
            <p style="text-align:center;margin:24px 0;">
              <a href="{link}" style="background:#2b6cb0;color:#ffffff;padding:12px 24px;
                 border-radius:6px;text-decoration:none;">Activar mi cuenta</a>
            </p>
            <p>O copia este enlace en tu navegador:<br/><span style="color:#4a5568;">{link}</span></p>
            <p style="color:#718096;">El enlace expira el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.
               Si no esperabas esta invitación, ignora este correo.</p>
            """
        );
        return new RenderedEmail(subject, html, text);
    }

    public static RenderedEmail PasswordReset(PortalOptions portal, string rawToken, DateTime expiresAtUtc)
    {
        var link = $"{portal.BaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        var subject = $"Restablece tu contraseña de {portal.ProductName}";
        var text =
            $"Recibimos una solicitud para restablecer tu contraseña.\n"
            + $"Enlace: {link}\nExpira el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC. "
            + "Si no fuiste tú, ignora este correo.";
        var html = Layout(
            subject,
            $"""
            <p>Recibimos una solicitud para restablecer tu contraseña.</p>
            <p style="text-align:center;margin:24px 0;">
              <a href="{link}" style="background:#2b6cb0;color:#ffffff;padding:12px 24px;
                 border-radius:6px;text-decoration:none;">Restablecer contraseña</a>
            </p>
            <p style="color:#718096;">El enlace expira el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.
               Si no solicitaste el cambio, ignora este correo: tu contraseña actual sigue siendo válida.</p>
            """
        );
        return new RenderedEmail(subject, html, text);
    }

    public static RenderedEmail OtpCode(PortalOptions portal, string code, string purpose)
    {
        var reason = purpose switch
        {
            "login" => "iniciar sesión",
            "email_change" => "confirmar tu nuevo email",
            "phone_verification" => "verificar tu teléfono",
            _ => "continuar",
        };
        var subject = $"{Encode(code)} es tu código de {portal.ProductName}";
        var text = $"Tu código para {reason} es: {code}. Expira en pocos minutos. No lo compartas con nadie.";
        var html = Layout(
            subject,
            $"""
            <p>Tu código de verificación para {reason} es:</p>
            <p style="text-align:center;font-size:32px;letter-spacing:8px;font-weight:bold;
               margin:24px 0;">{Encode(code)}</p>
            <p style="color:#718096;">Expira en pocos minutos. Nunca lo compartas:
               el equipo de {Encode(portal.ProductName)} jamás te lo pedirá.</p>
            """
        );
        return new RenderedEmail(subject, html, text);
    }

    public static RenderedEmail EmailChange(PortalOptions portal, string rawToken, DateTime expiresAtUtc)
    {
        var link = $"{portal.BaseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(rawToken)}";
        var subject = $"Confirma tu nuevo email en {portal.ProductName}";
        var text = $"Confirma tu nueva dirección de email: {link}\nExpira el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.";
        var html = Layout(
            subject,
            $"""
            <p>Solicitaste cambiar el email de tu cuenta. Confirma la nueva dirección:</p>
            <p style="text-align:center;margin:24px 0;">
              <a href="{link}" style="background:#2b6cb0;color:#ffffff;padding:12px 24px;
                 border-radius:6px;text-decoration:none;">Confirmar nuevo email</a>
            </p>
            <p style="color:#718096;">El enlace expira el {expiresAtUtc:yyyy-MM-dd HH:mm} UTC.
               Si no solicitaste este cambio, contacta al administrador de tu oficina.</p>
            """
        );
        return new RenderedEmail(subject, html, text);
    }

    public static RenderedEmail SecurityAlert(PortalOptions portal, string alertType, string? ipAddress)
    {
        var description = alertType switch
        {
            "token_reuse_detected" =>
                "detectamos un intento de reutilización de una sesión. Por seguridad cerramos esa sesión.",
            "account_locked_out" =>
                "tu cuenta fue bloqueada temporalmente por demasiados intentos fallidos de inicio de sesión.",
            "mfa_disabled" => "la verificación en dos pasos (MFA) fue desactivada en tu cuenta.",
            "password_changed" => "la contraseña de tu cuenta fue cambiada.",
            "email_changed" => "el email de tu cuenta fue cambiado.",
            _ => "se registró actividad de seguridad en tu cuenta.",
        };
        var ip = string.IsNullOrWhiteSpace(ipAddress) ? "" : $" Dirección IP: {Encode(ipAddress)}.";
        var subject = $"Alerta de seguridad en tu cuenta de {portal.ProductName}";
        var text = $"Alerta de seguridad: {description}{ip} Si no fuiste tú, cambia tu contraseña de inmediato.";
        var html = Layout(
            subject,
            $"""
            <p>Alerta de seguridad: {description}{ip}</p>
            <p style="color:#718096;">Si reconoces esta actividad puedes ignorar este correo.
               Si no fuiste tú, cambia tu contraseña de inmediato y contacta al administrador.</p>
            """
        );
        return new RenderedEmail(subject, html, text);
    }

    public static RenderedEmail Welcome(PortalOptions portal, string name)
    {
        var subject = $"¡Bienvenido a {portal.ProductName}!";
        var link = portal.BaseUrl.TrimEnd('/');
        var text = $"Hola {name}, tu cuenta en {portal.ProductName} está lista. Entra en {link}.";
        var html = Layout(
            subject,
            $"""
            <p>Hola {Encode(name)}, tu cuenta está lista.</p>
            <p style="text-align:center;margin:24px 0;">
              <a href="{link}" style="background:#2b6cb0;color:#ffffff;padding:12px 24px;
                 border-radius:6px;text-decoration:none;">Ir a {Encode(portal.ProductName)}</a>
            </p>
            """
        );
        return new RenderedEmail(subject, html, text);
    }

    private static string Layout(string title, string body) =>
        $"""
            <!doctype html>
            <html><body style="margin:0;padding:0;background:#f7fafc;font-family:Arial,Helvetica,sans-serif;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
              <tr><td align="center" style="padding:32px 16px;">
                <table role="presentation" width="560" cellpadding="0" cellspacing="0"
                       style="background:#ffffff;border-radius:8px;padding:32px;color:#1a202c;font-size:15px;line-height:1.6;">
                  <tr><td>
                    <h2 style="color:#1a365d;margin-top:0;">{Encode(title)}</h2>
                    {body}
                    <hr style="border:none;border-top:1px solid #e2e8f0;margin:24px 0;"/>
                    <p style="color:#a0aec0;font-size:12px;">Este es un mensaje automático; no respondas a este correo.</p>
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body></html>
            """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
