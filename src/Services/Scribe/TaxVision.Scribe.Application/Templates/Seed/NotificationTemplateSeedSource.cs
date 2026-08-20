using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// Definición de un template a sembrar en Scribe (Fase 8 — migración desde Notification): el
/// EventKey por el que Notification renderiza, el TemplateKey heredado del catálogo viejo
/// (<c>EmailTemplates</c>/<c>SignatureTemplateCatalog</c> — mismo string, para no romper la
/// auditoría existente en NotificationLog.TemplateKey), y el HTML/subject Fluid sobre el layout
/// <c>system-base</c>. El HTML es solo el contenido interno: la cáscara (barra, card, wordmark,
/// footer) la aporta el base layout. Cada template abre con eyebrow + acento + H1 (el layout no
/// puede leer el eyebrow, ver BuildLayoutVariables). Idioma por defecto: inglés — los eventos de los
/// 17 templates no-Signature no llevan idioma del destinatario, así que serían ramas muertas. Los 6
/// de Signature sí son bilingües porque su evento trae <c>signer.Language</c>.
/// </summary>
public sealed record NotificationTemplateSeed(
    string EventKey,
    string TemplateKey,
    string Name,
    string Subject,
    string Html,
    IReadOnlyList<(string Name, VariableType Type, bool Required, string? DefaultValue, string? Description)> Variables,
    // Subir esto cuando cambie el HTML/subject del seed: el seeder republica una versión nueva
    // si supera al SeedContentVersion guardado (política "código manda" para System).
    int ContentVersion = 1
);

/// <summary>
/// Los 23 templates que Notification renderizaba localmente convertidos a Fluid HTML sobre
/// <c>system-base</c>. Sembrados por <c>ScribeNotificationTemplateSeeder</c> al arranque. El texto
/// plano no se declara aparte — el renderer cae a strip-tags automático del HTML cuando
/// <c>TextFileId</c> es null (ver FluidTemplateRenderer.ResolveTextAsync).
/// </summary>
public static class NotificationTemplateSeedSource
{
    // Propiedad computada (no field initializer): "All" aparece antes que las definiciones en este
    // archivo, y los field initializers de una clase estática corren en orden textual — un
    // `{ get; } = [...]` aquí capturaría null en cada una. `=>` evalúa on-access, ya inicializado.
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
            TaskWaitingOnClient,
            AppointmentScheduled,
            AppointmentRescheduled,
            AppointmentCancelled,
            ClientRequestCreated,
            ClientRequestDocumentRejected,
        ];

    private static NotificationTemplateSeed Invitation { get; } =
        new(
            EventKey: "auth.invitation_created.v1",
            TemplateKey: "auth.invitation",
            Name: "Auth — Invitación",
            Subject: "{% if is_resend %}Reminder: your invitation to {{ office }} on {{ product_name }}{% else %}You've been invited to {{ office }} on {{ product_name }}{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Invitation</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You've been invited to collaborate</td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">{{ inviter }}</strong> invited you to join <strong style="color:#1E466B;">{{ office }}</strong> on {{ product_name }}.</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Activate your account to access the workspace and start collaborating.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ invite_link }}" style="height:46px;v-text-anchor:middle;width:230px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Activate my account</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Activate my account</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ invite_link }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This link expires on {{ expires_at }} UTC. If you weren't expecting this invitation, you can ignore this email.</td></tr>
            </table>
            """,
            Variables:
            [
                ("office", VariableType.String, true, null, "Nombre del tenant o del producto si no hay tenant."),
                ("inviter", VariableType.String, true, null, "Nombre de quien invita."),
                ("invite_link", VariableType.Url, true, null, "URL de aceptación de la invitación."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("is_resend", VariableType.Bool, true, "false", "true si es un reenvío del mismo invite."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed PasswordReset { get; } =
        new(
            EventKey: "auth.password_reset_requested.v1",
            TemplateKey: "auth.password_reset",
            Name: "Auth — Restablecer contraseña",
            Subject: "Reset your {{ product_name }} password",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Reset your password</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">We received a request to reset the password for your <strong style="color:#23384B;">{{ product_name }}</strong> account.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ reset_link }}" style="height:46px;v-text-anchor:middle;width:220px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Reset password</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ reset_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Reset password</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ reset_link }}</span></td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This link expires on {{ expires_at }} UTC. If you didn't request this, ignore this email: your current password stays valid.</td></tr>
            </table>
            """,
            Variables:
            [
                ("reset_link", VariableType.Url, true, null, "URL de restablecimiento de contraseña."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed OtpCode { get; } =
        new(
            EventKey: "auth.mfa_otp_requested.v1",
            TemplateKey: "auth.otp_code",
            Name: "Auth — Código OTP",
            Subject: "{{ code }} is your {{ product_name }} code",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Verification</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your verification code</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Your verification code for <strong style="color:#23384B;">{{ reason }}</strong> is:</td></tr>
              <tr><td style="padding:14px 0 18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:12px;"><tr><td align="center" style="padding:22px 16px 6px 16px;font-family:Arial,Helvetica,sans-serif;font-size:34px;line-height:40px;letter-spacing:10px;font-weight:bold;color:#1E466B;mso-line-height-rule:exactly;">{{ code }}</td></tr><tr><td align="center" style="padding:0 16px 18px 16px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#70869A;mso-line-height-rule:exactly;">Valid for a few minutes</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Never share this code. The {{ product_name }} team will never ask you for it by phone, chat, or email.</td></tr>
            </table>
            """,
            Variables:
            [
                ("code", VariableType.String, true, null, "Código OTP de un solo uso."),
                ("reason", VariableType.String, true, null, "Motivo en texto ya traducido (p. ej. 'sign in')."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed EmailChange { get; } =
        new(
            EventKey: "auth.email_change_requested.v1",
            TemplateKey: "auth.email_change",
            Name: "Auth — Confirmar cambio de email",
            Subject: "Confirm your new email on {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Confirm your new email</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">You requested to change your account email. Confirm the new address to activate it:</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ confirm_link }}" style="height:46px;v-text-anchor:middle;width:220px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Confirm new email</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ confirm_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Confirm new email</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This link expires on {{ expires_at }} UTC. If you didn't request this change, contact your office administrator.</td></tr>
            </table>
            """,
            Variables:
            [
                ("confirm_link", VariableType.Url, true, null, "URL de confirmación del nuevo email."),
                ("expires_at", VariableType.String, true, null, "Fecha de expiración ya formateada (UTC)."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed SecurityAlert { get; } =
        new(
            EventKey: "auth.email_change_security_alert.v1",
            TemplateKey: "auth.security_alert",
            Name: "Auth — Alerta de seguridad",
            Subject: "Security alert on your {{ product_name }} account",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Security alert</td></tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FDEEEE" style="background-color:#FDEEEE;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #D65B5B;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">{{ description }}{% if ip_address != blank %}<br />IP address: <strong>{{ ip_address }}</strong>.{% endif %}</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you recognize this activity you can ignore this email. If it wasn't you, change your password immediately and contact your administrator.</td></tr>
            </table>
            """,
            Variables:
            [
                ("description", VariableType.String, true, null, "Descripción del evento de seguridad."),
                ("ip_address", VariableType.String, false, null, "IP de origen, si está disponible."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed TenantRecovery { get; } =
        new(
            EventKey: "auth.tenant_recovery_requested.v1",
            TemplateKey: "auth.tenant_recovery",
            Name: "Auth — Encuentra tu oficina",
            Subject: "Your offices on {{ product_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Account</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your offices</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">We found the following offices linked to your email:</td></tr>
              {% for office in offices %}
              <tr><td style="padding-bottom:8px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:12px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:20px;color:#23384B;mso-line-height-rule:exactly;"><a href="{{ office.url }}" style="color:#1E466B;text-decoration:none;font-weight:bold;">{{ office.name }}</a><br /><span style="font-size:12px;color:#70869A;word-break:break-all;">{{ office.url }}</span></td></tr></table></td></tr>
              {% endfor %}
              <tr><td style="padding-top:6px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you don't recognize this request, you can ignore this email.</td></tr>
            </table>
            """,
            Variables:
            [
                ("offices", VariableType.String, true, null, "Lista de objetos { name, url } por oficina."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed Welcome { get; } =
        new(
            EventKey: "auth.user_registered.v1",
            TemplateKey: "auth.welcome",
            Name: "Auth — Bienvenida",
            Subject: "Welcome to {{ product_name }}!",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Welcome</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Welcome aboard!</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ name }}</strong>, your account is ready. Sign in and get started.</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Go to {{ product_name }}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Go to {{ product_name }}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("name", VariableType.String, true, null, "Nombre del usuario."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed SignatureInvitation { get; } =
        new(
            EventKey: "sig.signer_invited.v1",
            TemplateKey: "sig.invitation.v1",
            Name: "Signature — Invitación a firmar",
            Subject: "{% if language == 'Es' %}TaxProffice — Solicitud de firma pendiente{% else %}TaxProffice — Signature request pending{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tienes una firma pendiente</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, tienes una solicitud de firma pendiente en TaxProffice.{% if requires_consent %} Se te pedirá aceptar el consentimiento antes de firmar.{% endif %}</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You have a pending signature</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, you have a pending signature request on TaxProffice.{% if requires_consent %} You'll be asked to accept the consent before signing.{% endif %}</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ invite_link }}" style="height:46px;v-text-anchor:middle;width:240px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}El enlace vence el {{ expires_at }} UTC.{% else %}The link expires on {{ expires_at }} UTC.{% endif %}</td></tr>
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
            Subject: "{% if language == 'Es' %}TaxProffice — Recordatorio: tu firma sigue pendiente{% else %}TaxProffice — Reminder: your signature is still pending{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tu firma sigue pendiente</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, este es un recordatorio ({{ reminders_sent }} de 3) de que tu firma sigue pendiente en TaxProffice. El enlace vence el {{ expires_at }} UTC.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your signature is still pending</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, this is reminder {{ reminders_sent }} of 3 that your signature is still pending on TaxProffice. The link expires on {{ expires_at }} UTC.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ invite_link }}" style="height:46px;v-text-anchor:middle;width:240px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ invite_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">{% if language == 'Es' %}Abrir solicitud de firma{% else %}Open signature request{% endif %}</a></td></tr></table>
                  <!--<![endif]-->
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
            Subject: "{% if language == 'Es' %}TaxProffice — Firma completada{% else %}TaxProffice — Signature completed{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Firma completada</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, el proceso de firma se completó exitosamente el {{ completed_at }} UTC. No necesitas hacer nada más.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Signature completed</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, the signature process was completed successfully on {{ completed_at }} UTC. No further action is needed.</td></tr>
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
            Subject: "{% if language == 'Es' %}TaxProffice — Solicitud de firma expirada{% else %}TaxProffice — Signature request expired{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Solicitud de firma expirada</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, la solicitud de firma venció el {{ expired_at }} UTC sin completarse. Si todavía necesitas firmar, contacta a quien te la envió para que genere una nueva solicitud.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Signature request expired</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, the signature request expired on {{ expired_at }} UTC without being completed. If you still need to sign, contact the sender to issue a new request.</td></tr>
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
            Subject: "{% if language == 'Es' %}TaxProffice — Solicitud de firma cancelada{% else %}TaxProffice — Signature request cancelled{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Firma{% else %}Signature{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Solicitud de firma cancelada</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, uno de los firmantes rechazó firmar el documento, por lo que la solicitud fue cancelada. No necesitas hacer nada más.</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Signature request cancelled</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, one of the signers declined to sign the document, so the request was cancelled. No further action is needed.</td></tr>
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
            Subject: "{% if language == 'Es' %}TaxProffice — Código de verificación{% else %}TaxProffice — Verification code{% endif %}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Verificación{% else %}Verification{% endif %}</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              {% if language == 'Es' %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Tu código de verificación</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hola <strong style="color:#23384B;">{{ full_name }}</strong>, tu código de verificación de TaxProffice es:</td></tr>
              {% else %}
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your verification code</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ full_name }}</strong>, your TaxProffice verification code is:</td></tr>
              {% endif %}
              <tr><td style="padding:14px 0 18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:12px;"><tr><td align="center" style="padding:22px 16px;font-family:Arial,Helvetica,sans-serif;font-size:34px;line-height:40px;letter-spacing:10px;font-weight:bold;color:#1E466B;mso-line-height-rule:exactly;">{{ code }}</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">{% if language == 'Es' %}Vence el {{ expires_at }} UTC. Si no solicitaste este código, ignora este correo.{% else %}Expires on {{ expires_at }} UTC. If you did not request this code, please ignore this email.{% endif %}</td></tr>
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

    // PayFlow — Auth publica estos 2 eventos pre-tenant (TenantId=Guid.Empty) durante el flujo
    // pago-primero. El firstName ya viene resuelto con fallback desde el consumer.
    private static NotificationTemplateSeed OnboardingOtpRequested { get; } =
        new(
            EventKey: "onboarding.otp_requested.v1",
            TemplateKey: "onboarding.otp_code",
            Name: "Onboarding — Código de verificación",
            Subject: "{{ otp_code }} is your {{ product_name }} verification code",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Verification</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your verification code</td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your code to continue signing up for {{ product_name }} is:</td></tr>
              <tr><td style="padding:14px 0 18px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:12px;"><tr><td align="center" style="padding:22px 16px;font-family:Arial,Helvetica,sans-serif;font-size:34px;line-height:40px;letter-spacing:10px;font-weight:bold;color:#1E466B;mso-line-height-rule:exactly;">{{ otp_code }}</td></tr></table></td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Expires in {{ expires_in_minutes }} minutes. Never share it: the {{ product_name }} team will never ask you for it.</td></tr>
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
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed OnboardingRegistrationReady { get; } =
        new(
            EventKey: "onboarding.registration_ready.v1",
            TemplateKey: "onboarding.registration_ready",
            Name: "Onboarding — Completar registro",
            Subject: "Your payment is confirmed — complete your {{ product_name }} account",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Welcome</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Complete your account</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, we confirmed your payment of <strong style="color:#1E466B;">{{ price_formatted }}</strong> on {{ paid_at }} for the {{ plan_name }} plan. You can now complete your account:</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 18px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ registration_url }}" style="height:46px;v-text-anchor:middle;width:210px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Complete my account</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ registration_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Complete my account</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              {% if receipt_download_url != blank %}
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:13px;line-height:20px;"><a href="{{ receipt_download_url }}" target="_blank" style="color:#1E466B;text-decoration:underline;">Download receipt</a></td></tr>
              {% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">If you don't recognize this purchase, contact support immediately.</td></tr>
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
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed OnboardingReceiptReady { get; } =
        new(
            EventKey: "onboarding.receipt_ready.v1",
            TemplateKey: "onboarding.receipt_ready",
            Name: "Onboarding — Recibo disponible",
            Subject: "Your {{ product_name }} payment receipt is ready",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Receipt</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your receipt is ready</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ first_name }}</strong>, your {{ product_name }} payment receipt is ready to download:</td></tr>
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ receipt_download_url }}" style="height:46px;v-text-anchor:middle;width:190px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Download receipt</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ receipt_download_url }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Download receipt</a></td></tr></table>
                  <!--<![endif]-->
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
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    /// <summary>
    /// Reminder Fase 10 — el template que la Fase 8 difirió a propósito hasta que Notification tuvo el
    /// directorio <c>userId → email</c>. El H1 usa el {{ title }} del recordatorio.
    /// </summary>
    private static NotificationTemplateSeed ReminderDue { get; } =
        new(
            EventKey: "reminder.due.v1",
            TemplateKey: "reminder.due",
            Name: "Reminder — Recordatorio vencido",
            Subject: "{{ title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Reminder</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:14px;font-family:Arial,Helvetica,sans-serif;font-size:24px;line-height:32px;font-weight:bold;letter-spacing:-0.3px;color:#23384B;mso-line-height-rule:exactly;">{{ title }}</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">{{ body }}</td></tr>
              {% if snooze_count > 0 %}
              <tr><td style="padding-top:4px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#70869A;mso-line-height-rule:exactly;">Snoozed {{ snooze_count }} time(s).</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:26px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Open {{ product_name }}</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Open {{ product_name }}</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                (
                    "title",
                    VariableType.String,
                    true,
                    null,
                    "Título del recordatorio (con el sufijo de pospuesto si aplica)."
                ),
                (
                    "body",
                    VariableType.String,
                    true,
                    null,
                    "Cuerpo del usuario, o la hora del ancla en su zona horaria."
                ),
                ("category", VariableType.String, true, "General", "General | Calendar | Task | Note."),
                ("snooze_count", VariableType.Number, true, "0", "Cuántas veces se pospuso."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, "TaxProffice", "Branding del producto."),
            ]
        );

    private static NotificationTemplateSeed TaskWaitingOnClient { get; } =
        new(
            EventKey: "task.waiting_on_client.v1",
            TemplateKey: "task.waiting_on_client.v1",
            Name: "Task — Documentación pendiente del cliente",
            Subject: "{{ product_name }} — We're missing documents from you",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Documents</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">We're missing some documents</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, to continue with <strong style="color:#1E466B;">{{ task_title }}</strong>{% if tax_year %} (tax year {{ tax_year }}){% endif %} we need you to send us:</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">{{ expected_items }}</td></tr></table></td></tr>
              {% if client_due_at_utc %}
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">Please send it before <strong style="color:#23384B;">{{ client_due_at_utc }}</strong>.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:24px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Upload documents</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Upload documents</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Nombre del cliente, del directorio local."),
                ("task_title", VariableType.String, true, null, "Título de la tarea que quedó esperando."),
                (
                    "expected_items",
                    VariableType.String,
                    true,
                    null,
                    "Qué se le pide al cliente, tal como lo escribió el preparador."
                ),
                (
                    "client_due_at_utc",
                    VariableType.String,
                    false,
                    null,
                    "Para cuándo se le pide; distinta del vencimiento de la tarea."
                ),
                ("tax_year", VariableType.Number, false, null, "Año fiscal de la tarea, si tiene."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal del cliente."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    /// <summary>
    /// La invitación. La hora va con su zona escrita al lado: «10:00» a secas es exactamente lo que
    /// hace que alguien se presente con una hora de diferencia.
    /// </summary>
    private static NotificationTemplateSeed AppointmentScheduled { get; } =
        new(
            EventKey: "calendar.appointment_scheduled.v1",
            TemplateKey: "calendar.appointment_scheduled.v1",
            Name: "Calendar — Te agendaron una cita",
            Subject: "{{ product_name }} — {{ appointment_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">You have a new appointment</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">You've been scheduled for: <strong style="color:#1E466B;">{{ appointment_title }}</strong>.</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;"><strong>{{ start_local }}</strong> <span style="color:#70869A;">({{ time_zone }})</span></td></tr></table></td></tr>
              {% if is_recurring %}<tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">It repeats: check the calendar for all dates.</td></tr>{% endif %}
              {% if is_virtual %}<tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">It's a video meeting; the link is in the appointment.</td></tr>{% endif %}
            </table>
            """,
            Variables:
            [
                ("appointment_title", VariableType.String, true, null, "Titulo de la cita."),
                ("start_local", VariableType.String, true, null, "Inicio en la zona de la cita, ya formateado."),
                ("time_zone", VariableType.String, true, null, "Zona de la cita, escrita al lado de la hora."),
                ("is_recurring", VariableType.Bool, false, null, "Si la cita se repite."),
                ("is_virtual", VariableType.Bool, false, null, "Si es una reunion por video."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    /// <summary>
    /// El cambio de hora lleva la vieja y la nueva. Decir solo la nueva obliga a recordar cuál era, y
    /// quien tiene ocho citas esa semana no la recuerda.
    /// </summary>
    private static NotificationTemplateSeed AppointmentRescheduled { get; } =
        new(
            EventKey: "calendar.appointment_rescheduled.v1",
            TemplateKey: "calendar.appointment_rescheduled.v1",
            Name: "Calendar — Se movió tu cita",
            Subject: "{{ product_name }} — Your appointment moved",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your appointment moved</td></tr>
              {% if previous_local %}<tr><td style="padding-bottom:10px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#70869A;mso-line-height-rule:exactly;">Before: <s>{{ previous_local }}</s></td></tr>{% endif %}
              <tr><td style="padding-bottom:4px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:22px;color:#23384B;mso-line-height-rule:exactly;">Now: <strong>{{ new_local }}</strong> <span style="color:#70869A;">({{ time_zone }})</span></td></tr></table></td></tr>
            </table>
            """,
            Variables:
            [
                ("scope", VariableType.String, false, null, "Si se movio una ocurrencia o la serie."),
                ("previous_local", VariableType.String, false, null, "La hora vieja, para no obligar a recordarla."),
                ("new_local", VariableType.String, true, null, "La hora nueva, en la zona de la cita."),
                ("time_zone", VariableType.String, true, null, "Zona de la cita."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    /// <summary>El aviso que más importa: no mandarlo deja a alguien presentándose a algo que no existe.</summary>
    private static NotificationTemplateSeed AppointmentCancelled { get; } =
        new(
            EventKey: "calendar.appointment_cancelled.v1",
            TemplateKey: "calendar.appointment_cancelled.v1",
            Name: "Calendar — Se canceló tu cita",
            Subject: "{{ product_name }} — Your appointment was cancelled",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Appointment</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your appointment was cancelled</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">An appointment you had scheduled was cancelled.</td></tr>
              {% if reason %}<tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FDEEEE" style="background-color:#FDEEEE;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #D65B5B;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">{{ reason }}</td></tr></table></td></tr>{% endif %}
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">No action is needed.</td></tr>
            </table>
            """,
            Variables:
            [
                ("scope", VariableType.String, false, null, "Si se cancelo una ocurrencia o la serie."),
                ("reason", VariableType.String, false, null, "Motivo, si el organizador lo escribio."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    private static NotificationTemplateSeed ClientRequestCreated { get; } =
        new(
            EventKey: "task.client_request_created.v1",
            TemplateKey: "task.client_request_created.v1",
            Name: "Task — Tu contador te pidió documentación",
            Subject: "{{ product_name }} — {{ request_title }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Documents</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your accountant requested documents</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, your accountant needs you to send:</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;"><strong>{{ request_title }}</strong>{% if request_details %}<br />{{ request_details }}{% endif %}</td></tr></table></td></tr>
              {% if due_at_utc %}
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#496174;mso-line-height-rule:exactly;">Please send it before <strong style="color:#23384B;">{{ due_at_utc }}</strong>.</td></tr>
              {% endif %}
              <tr>
                <td align="left" style="padding:24px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:200px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Upload in my portal</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Upload in my portal</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Nombre del cliente, del directorio local."),
                ("request_title", VariableType.String, true, null, "Qué se le pide, en el idioma del cliente."),
                ("request_details", VariableType.String, false, null, "Detalle opcional del pedido."),
                ("due_at_utc", VariableType.String, false, null, "La fecha que se le dio al cliente."),
                ("portal_link", VariableType.Url, true, null, "URL base del portal del cliente."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );

    /// <summary>
    /// El archivo del cliente no pasó el escaneo. <b>Aquí no entra el motivo técnico.</b> «Tiene un
    /// virus» no le dice al cliente qué hacer y filtra infraestructura; el preparador recibe el motivo
    /// real por otro canal.
    /// </summary>
    private static NotificationTemplateSeed ClientRequestDocumentRejected { get; } =
        new(
            EventKey: "task.client_request_document_rejected.v1",
            TemplateKey: "task.client_request_document_rejected.v1",
            Name: "Task — No pudimos procesar tu archivo",
            Subject: "{{ product_name }} — We couldn't process {{ document_name }}",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Documents</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:16px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">We couldn't process your file</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">Hi <strong style="color:#23384B;">{{ customer_name }}</strong>, {{ client_message }}</td></tr>
              <tr><td style="padding-bottom:12px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#FFF6E9" style="background-color:#FFF6E9;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #E8A13A;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:21px;color:#23384B;mso-line-height-rule:exactly;">File: <strong>{{ document_name }}</strong><br />Request: {{ request_title }}</td></tr></table></td></tr>
              <tr>
                <td align="left" style="padding:12px 0 4px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ portal_link }}" style="height:46px;v-text-anchor:middle;width:190px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Upload it again</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ portal_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Upload it again</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
            </table>
            """,
            Variables:
            [
                ("customer_name", VariableType.String, true, null, "Nombre del cliente, del directorio local."),
                ("document_name", VariableType.String, true, null, "Nombre del archivo que subió."),
                ("request_title", VariableType.String, true, null, "A qué pedido corresponde."),
                (
                    "client_message",
                    VariableType.String,
                    true,
                    null,
                    "Mensaje accionable para el cliente. Nunca el motivo técnico del rechazo."
                ),
                ("portal_link", VariableType.Url, true, null, "URL base del portal del cliente."),
                ("product_name", VariableType.String, true, null, "Nombre del producto en el asunto."),
            ]
        );
}
