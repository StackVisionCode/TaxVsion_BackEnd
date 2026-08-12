using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// Definición de un template a sembrar en Scribe (Fase 8 — migración desde Notification): el
/// EventKey por el que Notification renderiza, el TemplateKey heredado del catálogo viejo
/// (<c>EmailTemplates</c>/<c>SignatureTemplateCatalog</c> — mismo string, para no romper la
/// auditoría existente en NotificationLog.TemplateKey), y el HTML/subject Fluid ya migrado al
/// layout <c>system-base</c> (todos son System-scoped: el contenido original no tenía branding
/// por tenant).
/// </summary>
public sealed record NotificationTemplateSeed(
    string EventKey,
    string TemplateKey,
    string Name,
    string Subject,
    string Html,
    IReadOnlyList<(string Name, VariableType Type, bool Required, string? DefaultValue, string? Description)> Variables
);

/// <summary>
/// Los 13 templates que Notification renderizaba localmente (7 <c>auth.*</c> en
/// <c>EmailTemplates.cs</c> + 6 <c>sig.*.v1</c> en <c>SignatureTemplateCatalog.cs</c>) convertidos a
/// Fluid HTML sobre <c>system-base</c>. Sembrados por <c>ScribeNotificationTemplateSeeder</c> al
/// arranque. El texto plano no se declara aparte — el renderer cae a strip-tags automático del HTML
/// cuando <c>TextFileId</c> es null (ver FluidTemplateRenderer.ResolveTextAsync).
/// </summary>
public static class NotificationTemplateSeedSource
{
    // Propiedad computada (no field initializer): "All" aparece antes que las 13 definiciones en
    // este archivo, y los field initializers de una clase estática corren en orden textual — un
    // `{ get; } = [...]` aquí capturaría null en cada una (todavía no inicializadas). `=>` evalúa
    // on-access, momento en el que la clase ya terminó de inicializarse.
    public static IReadOnlyList<NotificationTemplateSeed> All =>
        [
            Invitation,
            PasswordReset,
            OtpCode,
            EmailChange,
            SecurityAlert,
            TenantRecovery,
            Welcome,
            SignatureInvitation,
            SignatureReminder,
            SignatureCompleted,
            SignatureExpired,
            SignatureDeclined,
            SignatureVerificationChallenge,
            OnboardingOtpRequested,
            OnboardingRegistrationReady,
            OnboardingReceiptReady,
            ReminderDue,
        ];

    private static NotificationTemplateSeed Invitation { get; } =
        new(
            EventKey: "auth.invitation_created.v1",
            TemplateKey: "auth.invitation",
            Name: "Auth — Invitación",
            Subject: "{% if is_resend %}Recordatorio: tu invitación a {{ office }} en {{ product_name }}{% else %}Has sido invitado a {{ office }} en {{ product_name }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  <strong>{{ inviter }}</strong> te invitó a unirte a <strong>{{ office }}</strong> en {{ product_name }}.
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">Activar mi cuenta</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
              <tr>
                <td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:18px;color:#4a5568;">
                  O copia este enlace en tu navegador:<br />
                  <span style="word-break:break-all;">{{ invite_link }}</span>
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  El enlace expira el {{ expires_at }} UTC. Si no esperabas esta invitación, ignora este correo.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("office", VariableType.String, true, null, "Nombre del tenant o del producto si no hay tenant."),
                ("inviter", VariableType.String, true, null, "Nombre de quien invita."),
                ("invite_link", VariableType.Url, true, null, "URL de aceptación de la invitación."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("is_resend", VariableType.Bool, true, "false", "true si es un reenvío del mismo invite."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed PasswordReset { get; } =
        new(
            EventKey: "auth.password_reset_requested.v1",
            TemplateKey: "auth.password_reset",
            Name: "Auth — Restablecer contraseña",
            Subject: "Restablece tu contraseña de {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Recibimos una solicitud para restablecer tu contraseña.
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ reset_link }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">Restablecer contraseña</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  El enlace expira el {{ expires_at }} UTC. Si no solicitaste el cambio, ignora este correo: tu contraseña actual sigue siendo válida.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("reset_link", VariableType.Url, true, null, "URL de restablecimiento de contraseña."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed OtpCode { get; } =
        new(
            EventKey: "auth.mfa_otp_requested.v1",
            TemplateKey: "auth.otp_code",
            Name: "Auth — Código OTP",
            Subject: "{{ code }} es tu código de {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Tu código de verificación para {{ reason }} es:
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:8px 0 20px 0;font-family:Arial,Helvetica,sans-serif;font-size:32px;letter-spacing:8px;font-weight:bold;color:#111111;">
                  {{ code }}
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  Expira en pocos minutos. Nunca lo compartas: el equipo de {{ product_name }} jamás te lo pedirá.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("code", VariableType.String, true, null, "Código OTP de un solo uso."),
                ("reason", VariableType.String, true, null, "Motivo en texto ya traducido (p. ej. 'iniciar sesión')."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed EmailChange { get; } =
        new(
            EventKey: "auth.email_change_requested.v1",
            TemplateKey: "auth.email_change",
            Name: "Auth — Confirmar cambio de email",
            Subject: "Confirma tu nuevo email en {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Solicitaste cambiar el email de tu cuenta. Confirma la nueva dirección:
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ confirm_link }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">Confirmar nuevo email</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  El enlace expira el {{ expires_at }} UTC. Si no solicitaste este cambio, contacta al administrador de tu oficina.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("confirm_link", VariableType.Url, true, null, "URL de confirmación del nuevo email."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed SecurityAlert { get; } =
        new(
            EventKey: "auth.email_change_security_alert.v1",
            TemplateKey: "auth.security_alert",
            Name: "Auth — Alerta de seguridad",
            Subject: "Alerta de seguridad en tu cuenta de {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Alerta de seguridad: {{ description }}{% if ip_address != blank %} Dirección IP: {{ ip_address }}.{% endif %}
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  Si reconoces esta actividad puedes ignorar este correo. Si no fuiste tú, cambia tu contraseña de inmediato y contacta al administrador.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("description", VariableType.String, true, null, "Descripción del evento de seguridad."),
                ("ip_address", VariableType.String, false, null, "IP de origen, si está disponible."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed TenantRecovery { get; } =
        new(
            EventKey: "auth.tenant_recovery_requested.v1",
            TemplateKey: "auth.tenant_recovery",
            Name: "Auth — Encuentra tu oficina",
            Subject: "Tus oficinas en {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Encontramos las siguientes oficinas asociadas a tu email:
                </td>
              </tr>
              {% for office in offices %}
              <tr>
                <td style="padding:4px 0;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;">
                  <a href="{{ office.url }}" style="color:#2b6cb0;text-decoration:underline;">{{ office.name }}</a>
                  — <span style="color:#718096;">{{ office.url }}</span>
                </td>
              </tr>
              {% endfor %}
              <tr>
                <td style="padding-top:12px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  Si no reconoces esta solicitud, ignora este correo.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("offices", VariableType.String, true, null, "Lista de objetos { name, url } por oficina."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed Welcome { get; } =
        new(
            EventKey: "auth.user_registered.v1",
            TemplateKey: "auth.welcome",
            Name: "Auth — Bienvenida",
            Subject: "¡Bienvenido a {{ product_name }}!",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Hola {{ name }}, tu cuenta está lista.
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">Ir a {{ product_name }}</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("name", VariableType.String, true, null, "Nombre del usuario."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed SignatureInvitation { get; } =
        new(
            EventKey: "sig.signer_invited.v1",
            TemplateKey: "sig.invitation.v1",
            Name: "Signature — Invitación a firmar",
            Subject: "{% if language == 'Es' %}TaxVision — Solicitud de firma pendiente{% else %}TaxVision — Signature request pending{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hola {{ full_name }},</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Tienes una solicitud de firma pendiente en TaxVision.{% if requires_consent %} Se te pedirá aceptar el consent antes de firmar.{% endif %}</td></tr>
              {% else %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hi {{ full_name }},</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">You have a pending signature request on TaxVision.{% if requires_consent %} You'll be asked to accept the consent before signing.{% endif %}</td></tr>
              {% endif %}
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  {% if language == 'Es' %}El enlace vence el {{ expires_at }} UTC.{% else %}The link expires on {{ expires_at }} UTC.{% endif %}
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("invite_link", VariableType.Url, true, null, "URL pública de firma."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("requires_consent", VariableType.Bool, true, "false", "true si requiere aceptar consent antes."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureReminder { get; } =
        new(
            EventKey: "sig.request_reminder_due.v1",
            TemplateKey: "sig.reminder.v1",
            Name: "Signature — Recordatorio de firma",
            Subject: "{% if language == 'Es' %}TaxVision — Recordatorio: tu firma sigue pendiente{% else %}TaxVision — Reminder: your signature is still pending{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hola {{ full_name }},</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Este es un recordatorio ({{ reminders_sent }} de 3) de que tu firma sigue pendiente en TaxVision. El enlace vence el {{ expires_at }} UTC.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hi {{ full_name }},</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">This is reminder {{ reminders_sent }} of 3 that your signature is still pending on TaxVision. The link expires on {{ expires_at }} UTC.</td></tr>
              {% endif %}
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("invite_link", VariableType.Url, true, null, "URL pública de firma."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("reminders_sent", VariableType.Number, true, null, "Número de recordatorio (1..3)."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureCompleted { get; } =
        new(
            EventKey: "sig.request_completed.v1",
            TemplateKey: "sig.completed.v1",
            Name: "Signature — Firma completada",
            Subject: "{% if language == 'Es' %}TaxVision — Firma completada{% else %}TaxVision — Signature completed{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hola {{ full_name }},</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">El proceso de firma se completó exitosamente el {{ completed_at }} UTC. No necesitas hacer nada más.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hi {{ full_name }},</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">The signature process was completed successfully on {{ completed_at }} UTC. No further action is needed.</td></tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("completed_at", VariableType.String, true, null, "Fecha de finalización ya formateada (UTC)."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureExpired { get; } =
        new(
            EventKey: "sig.request_expired.v1",
            TemplateKey: "sig.expired.v1",
            Name: "Signature — Solicitud expirada",
            Subject: "{% if language == 'Es' %}TaxVision — Solicitud de firma expirada{% else %}TaxVision — Signature request expired{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hola {{ full_name }},</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">La solicitud de firma venció el {{ expired_at }} UTC sin completarse. Si todavía necesitas firmar, contacta a quien te la envió para que genere una nueva solicitud.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hi {{ full_name }},</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">The signature request expired on {{ expired_at }} UTC without being completed. If you still need to sign, contact the sender to issue a new request.</td></tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("expired_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureDeclined { get; } =
        new(
            EventKey: "sig.signer_rejected.v1",
            TemplateKey: "sig.declined.v1",
            Name: "Signature — Solicitud cancelada",
            Subject: "{% if language == 'Es' %}TaxVision — Solicitud de firma cancelada{% else %}TaxVision — Signature request cancelled{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hola {{ full_name }},</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Uno de los firmantes rechazó firmar el documento, por lo que la solicitud fue cancelada. No necesitas hacer nada más.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hi {{ full_name }},</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">One of the signers declined to sign the document, so the request was cancelled. No further action is needed.</td></tr>
              {% endif %}
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante pendiente."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    private static NotificationTemplateSeed SignatureVerificationChallenge { get; } =
        new(
            EventKey: "sig.verification_challenge_issued.v1",
            TemplateKey: "sig.verification-challenge.v1",
            Name: "Signature — Código de verificación",
            Subject: "{% if language == 'Es' %}TaxVision — Código de verificación{% else %}TaxVision — Verification code{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hola {{ full_name }},</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Tu código de verificación TaxVision es:</td></tr>
              {% else %}
              <tr><td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Hi {{ full_name }},</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">Your TaxVision verification code is:</td></tr>
              {% endif %}
              <tr>
                <td align="center" style="padding:8px 0 16px 0;font-family:Arial,Helvetica,sans-serif;font-size:32px;letter-spacing:8px;font-weight:bold;color:#111111;">
                  {{ code }}
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  {% if language == 'Es' %}Vence el {{ expires_at }} UTC. Si no solicitaste este código, ignora este correo.{% else %}Expires on {{ expires_at }} UTC. If you did not request this code, please ignore this email.{% endif %}
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("full_name", VariableType.String, true, null, "Nombre completo del firmante."),
                ("code", VariableType.String, true, null, "Código de verificación de un solo uso."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("language", VariableType.String, true, "En", "'Es' o 'En'."),
            ]
        );

    // PayFlow (Fase 12) — Auth (Fase 5/9) publica estos 2 eventos pre-tenant (TenantId=Guid.Empty)
    // durante el flujo pago-primero. El firstName ya viene resuelto con fallback desde el consumer
    // (evt.FirstNameHint ?? "there" / evt.FirstName), igual que "office"/"inviter" en Invitation de
    // arriba — no se usa el filtro `default` de Fluid en ningún template existente de este archivo.
    private static NotificationTemplateSeed OnboardingOtpRequested { get; } =
        new(
            EventKey: "onboarding.otp_requested.v1",
            TemplateKey: "onboarding.otp_code",
            Name: "Onboarding — Código de verificación",
            Subject: "{{ otp_code }} es tu código de verificación de {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Hola {{ first_name }}, tu código de verificación para continuar con el alta en {{ product_name }} es:
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:8px 0 20px 0;font-family:Arial,Helvetica,sans-serif;font-size:32px;letter-spacing:8px;font-weight:bold;color:#111111;">
                  {{ otp_code }}
                </td>
              </tr>
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  Expira en {{ expires_in_minutes }} minutos. Nunca lo compartas: el equipo de {{ product_name }} jamás te lo pedirá.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("otp_code", VariableType.String, true, null, "Código OTP de un solo uso."),
                (
                    "first_name",
                    VariableType.String,
                    true,
                    null,
                    "Nombre del comprador (con fallback ya resuelto por el consumer)."
                ),
                ("expires_in_minutes", VariableType.Number, true, null, "Minutos hasta la expiración del código."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed OnboardingRegistrationReady { get; } =
        new(
            EventKey: "onboarding.registration_ready.v1",
            TemplateKey: "onboarding.registration_ready",
            Name: "Onboarding — Completar registro",
            Subject: "Tu pago fue confirmado — completa tu cuenta de {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Hola {{ first_name }}, confirmamos tu pago de {{ price_formatted }} el {{ paid_at }} para el plan {{ plan_name }}. Ya podés completar tu cuenta:
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ registration_url }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">Completar mi cuenta</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
              {% if receipt_download_url != blank %}
              <tr>
                <td align="center" style="padding:0 0 16px 0;">
                  <a href="{{ receipt_download_url }}" target="_blank" style="font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#2b6cb0;text-decoration:underline;">Descargar recibo</a>
                </td>
              </tr>
              {% endif %}
              <tr>
                <td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  Si no reconocés esta compra, contactá a soporte de inmediato.
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del comprador."),
                (
                    "plan_name",
                    VariableType.String,
                    true,
                    null,
                    "Nombre del plan (con fallback ya resuelto por el consumer)."
                ),
                ("price_formatted", VariableType.String, true, null, "Monto ya formateado (p. ej. '49.00 USD')."),
                ("paid_at", VariableType.String, true, null, "Fecha de pago ya formateada (UTC)."),
                (
                    "registration_url",
                    VariableType.Url,
                    true,
                    null,
                    "URL de registro con el raw token, resuelta vía Auth."
                ),
                (
                    "receipt_download_url",
                    VariableType.Url,
                    false,
                    null,
                    "Link mediador de descarga del recibo, si ya llegó."
                ),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    // Sigue a OnboardingRegistrationReady: en la práctica el PDF (Playwright + subida a
    // CloudStorage) siempre tarda más que el envío del email de bienvenida disparado por el mismo
    // pago, así que el botón de descarga condicional de ese email casi nunca sale — este es el
    // segundo envío que faltaba (ver comentario de cabecera del archivo de consumers).
    private static NotificationTemplateSeed OnboardingReceiptReady { get; } =
        new(
            EventKey: "onboarding.receipt_ready.v1",
            TemplateKey: "onboarding.receipt_ready",
            Name: "Onboarding — Recibo disponible",
            Subject: "Tu recibo de pago de {{ product_name }} ya está disponible",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  Hola {{ first_name }}, tu recibo de pago de {{ product_name }} ya está listo para descargar:
                </td>
              </tr>
              <tr>
                <td align="center" style="padding:0 0 16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ receipt_download_url }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">Descargar recibo</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("first_name", VariableType.String, true, null, "Nombre del comprador."),
                (
                    "receipt_download_url",
                    VariableType.Url,
                    true,
                    null,
                    "Link mediador de descarga del recibo (Auth), nunca vence."
                ),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

    /// <summary>
    /// Reminder Fase 10 — el template que la Fase 8 difirió a propósito: hasta que Notification tuvo
    /// el directorio <c>userId → email</c>, nadie podía invocarlo y sembrarlo habría sido superficie
    /// muerta (lección de la Fase 8 de Postmaster, implementada y luego retirada).
    /// </summary>
    private static NotificationTemplateSeed ReminderDue { get; } =
        new(
            EventKey: "reminder.due.v1",
            TemplateKey: "reminder.due",
            Name: "Reminder — Recordatorio vencido",
            Subject: "{{ title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr>
                <td style="padding-bottom:8px;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:22px;color:#1a202c;font-weight:bold;">
                  {{ title }}
                </td>
              </tr>
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#333333;">
                  {{ body }}
                </td>
              </tr>
              {% if snooze_count > 0 %}
              <tr>
                <td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#718096;">
                  Pospuesto {{ snooze_count }} vez(ces).
                </td>
              </tr>
              {% endif %}
              <tr>
                <td align="center" style="padding:16px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                    <tr>
                      <td align="center" bgcolor="#2b6cb0" style="background-color:#2b6cb0;border-radius:6px;">
                        <a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:12px 24px;font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#ffffff;text-decoration:none;font-weight:bold;">Abrir {{ product_name }}</a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("title", VariableType.String, true, null, "Título del recordatorio (con el sufijo de pospuesto si aplica)."),
                ("body", VariableType.String, true, null, "Cuerpo del usuario, o la hora del ancla en su zona horaria."),
                ("category", VariableType.String, true, "General", "General | Calendar | Task | Note."),
                ("snooze_count", VariableType.Number, true, "0", "Cuántas veces se pospuso."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, "TaxVision", "Branding del producto."),
            ]
        );

}
