using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Seed;

/// <summary>
/// Avisos del estado de la credencial puesto por la oficina (04 §6.4–6.6). La cuenta asegurada lleva el link seguro:
/// <c>system-base</c>, carril del sistema. El cambio obligatorio y la contraseña cambiada no llevan token:
/// <c>tenant-base</c>, buzón de la oficina con caída al sistema (DM-02). Nunca el nombre de quien actuó ni el motivo.
/// </summary>
public static partial class NotificationTemplateSeedSource
{
    /// <summary>Plantillas nuevas: versión propia, por encima de las demás.</summary>
    private const int CredentialStateNoticesVersion = 44;

    // tenant_name lo inyecta el renderer en tenant-base; sin proyección de la oficina queda "your office".
    private const string TenantOfficeAssign =
        """{% if tenant_name %}{% assign office = tenant_name %}{% else %}{% assign office = "your office" %}{% endif %}""";

    private static (string, VariableType, bool, string?, string?) ProductNameVariable =>
        (
            "product_name",
            VariableType.String,
            false,
            "TaxOffix",
            "Branding del producto (lo inyecta el renderer desde PlatformSettings)."
        );

    private static (string, VariableType, bool, string?, string?) AccountKindVariable =>
        ("account_kind", VariableType.String, false, "workspace", "portal o workspace: de qué cuenta es el aviso.");

    private static (string, VariableType, bool, string?, string?) TenantNameVariable =>
        (
            "tenant_name",
            VariableType.String,
            false,
            null,
            "Nombre de la oficina; lo inyecta el renderer (tenant-base)."
        );

    private static (string, VariableType, bool, string?, string?) OfficeAliasVariable =>
        ("office", VariableType.String, false, null, "Lo arma la plantilla desde tenant_name; no se envía.");

    private static (string, VariableType, bool, string?, string?) SignInLinkVariable =>
        (
            "sign_in_link",
            VariableType.Url,
            false,
            null,
            "Login del subdominio de la oficina. Vacío = sin botón (nunca otra base)."
        );

    // Cuenta asegurada (04 §6.5): link seguro de origen Incident.
    private static NotificationTemplateSeed AccountSecured { get; } =
        new(
            EventKey: "auth.account_secured.v1",
            TemplateKey: "auth.account-secured",
            Name: "Auth — Cuenta asegurada por la oficina",
            Subject: "Action needed: your {{ office }} account was secured",
            Html: """
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
              <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your account was secured</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">An office administrator at <strong style="color:#23384B;">{{ office }}</strong> secured your {% if account_kind == 'portal' %}client portal account{% else %}{{ product_name }} team account{% endif %} as a precaution. This usually happens when there's a concern that someone else could access it.</td></tr>
              <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">What changed:</strong></td></tr>
              <tr><td style="padding-bottom:4px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">&bull; Your previous password no longer works.</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">&bull; You've been signed out of all devices.</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">To get back in, set a new password:</td></tr>
              <tr>
                <td align="left" style="padding:22px 0 22px 0;">
                  <!--[if mso]>
                  <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{{ reset_link }}" style="height:46px;v-text-anchor:middle;width:220px;" arcsize="22%" strokecolor="#1E466B" fillcolor="#1E466B"><w:anchorlock/><center style="color:#FFFFFF;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">Set a new password</center></v:roundrect>
                  <![endif]-->
                  <!--[if !mso]><!-- -->
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ reset_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Set a new password</a></td></tr></table>
                  <!--<![endif]-->
                </td>
              </tr>
              <tr><td style="padding-bottom:18px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;"><tr><td style="padding:14px 18px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">Button not working? Copy this link into your browser:<br /><span style="word-break:break-all;color:#1E466B;">{{ reset_link }}</span></td></tr></table></td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;mso-line-height-rule:exactly;">This link expires {{ expires_at | office_deadline }} and can be used once. If it expires, use "Forgot password?" on the {{ office }} sign-in page.</td></tr>
              <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;mso-line-height-rule:exactly;">Choose a password you don't use anywhere else. If you used the same password on other sites or with other offices, change it there too.</td></tr>
              <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">Didn't expect this, or have questions?</strong> Contact {{ office }} directly using the phone number or email you usually use with them.</td></tr>
              <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This email is about your account at {{ office }} on {{ product_name }}. It doesn't affect any other office you may use. {{ product_name }} and {{ office }} will never ask you for your password by email or phone.</td></tr>
            </table>
            """,
            Variables:
            [
                ("reset_link", VariableType.Url, true, null, "URL del link seguro para crear la contraseña nueva."),
                (
                    "expires_at",
                    VariableType.Date,
                    true,
                    null,
                    "Instante tipado: Scribe lo imprime como plazo en la zona de quien lee."
                ),
                ProductNameVariable,
                AccountKindVariable,
                ("office", VariableType.String, false, "your office", "Nombre de la oficina de esa cuenta."),
            ],
            ContentVersion: CredentialStateNoticesVersion
        );

    // Cambio obligatorio (04 §6.4): sin token y sin el motivo (puede ser sensible).
    private static NotificationTemplateSeed PasswordChangeRequiredNotice { get; } =
        new(
            EventKey: "auth.password_change_required.v1",
            TemplateKey: "auth.password-change-required",
            Name: "Auth — Cambio de contraseña pedido por la oficina",
            Subject: "{% if tenant_name %}{{ tenant_name }}{% else %}Your office{% endif %} asks you to update your password",
            Html: TenantOfficeAssign
                + """

                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                  <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
                  <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
                  <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Update your password</td></tr>
                  <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">An office administrator at <strong style="color:#23384B;">{{ office }}</strong> asked you to choose a new password for your {% if account_kind == 'portal' %}client portal account{% else %}{{ product_name }} team account{% endif %}.</td></tr>
                  <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">For your security, you've been signed out of all devices. Next time you sign in with your current password, we'll ask you to set a new one before you continue.</td></tr>
                  {% if sign_in_link %}
                  <tr>
                    <td align="left" style="padding:22px 0 22px 0;">
                      <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ sign_in_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Sign in to {{ office }}</a></td></tr></table>
                    </td>
                  </tr>
                  {% endif %}
                  <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;mso-line-height-rule:exactly;">Forgot your current password? Use "Forgot password?" on the sign-in page.</td></tr>
                  <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">Didn't expect this?</strong> Contact {{ office }} directly using the phone number or email you usually use with them.</td></tr>
                  <tr><td style="font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This email is about your account at {{ office }} on {{ product_name }}. It doesn't affect any other office you may use. {{ product_name }} and {{ office }} will never ask you for your password by email or phone.</td></tr>
                </table>
                """,
            Variables:
            [
                TenantNameVariable,
                OfficeAliasVariable,
                SignInLinkVariable,
                ProductNameVariable,
                AccountKindVariable,
            ],
            ContentVersion: CredentialStateNoticesVersion,
            LayoutKey: "tenant-base"
        );

    // Contraseña cambiada (04 §6.6): confirmación sin token.
    private static NotificationTemplateSeed PasswordChangedNotice { get; } =
        new(
            EventKey: "auth.password_changed.v1",
            TemplateKey: "auth.password-changed",
            Name: "Auth — Contraseña cambiada",
            Subject: "Your {% if tenant_name %}{{ tenant_name }} {% endif %}password was changed",
            Html: TenantOfficeAssign
                + """

                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                  <tr><td style="padding-bottom:2px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:16px;letter-spacing:1.2px;text-transform:uppercase;color:#70869A;mso-line-height-rule:exactly;">Security</td></tr>
                  <tr><td style="padding:6px 0 16px 0;"><table role="presentation" width="40" cellpadding="0" cellspacing="0" border="0"><tr><td height="3" bgcolor="#67BAF4" style="background-color:#67BAF4;height:3px;line-height:3px;font-size:0;">&nbsp;</td></tr></table></td></tr>
                  <tr><td style="padding-bottom:18px;font-family:Arial,Helvetica,sans-serif;font-size:26px;line-height:34px;font-weight:bold;letter-spacing:-0.4px;color:#23384B;mso-line-height-rule:exactly;">Your password was changed</td></tr>
                  <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">The password for your {% if account_kind == 'portal' %}client portal account{% else %}{{ product_name }} team account{% endif %} at <strong style="color:#23384B;">{{ office }}</strong> was changed{% if changed_at %} on {{ changed_at | office_date }}{% endif %}.</td></tr>
                  <tr><td style="padding-bottom:12px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;mso-line-height-rule:exactly;">You've been signed out of your other devices. Sign in again with your new password.</td></tr>
                  <tr><td style="padding-bottom:6px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:22px;color:#496174;mso-line-height-rule:exactly;"><strong style="color:#23384B;">Wasn't you?</strong> Use "Forgot password?" on the {{ office }} sign-in page right away to set a new password, then contact {{ office }} directly using the phone number or email you usually use with them.</td></tr>
                  {% if sign_in_link %}
                  <tr>
                    <td align="left" style="padding:22px 0 22px 0;">
                      <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#1E466B" style="background-color:#1E466B;border-radius:10px;"><a href="{{ sign_in_link }}" target="_blank" style="display:inline-block;padding:14px 32px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:18px;font-weight:bold;color:#FFFFFF;text-decoration:none;border-radius:10px;">Go to {{ office }} sign-in</a></td></tr></table>
                    </td>
                  </tr>
                  {% endif %}
                  <tr><td style="padding-top:12px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#70869A;mso-line-height-rule:exactly;">This email is about your account at {{ office }} on {{ product_name }}. It doesn't affect any other office you may use. {{ product_name }} and {{ office }} will never ask you for your password by email or phone.</td></tr>
                </table>
                """,
            Variables:
            [
                TenantNameVariable,
                OfficeAliasVariable,
                SignInLinkVariable,
                ("changed_at", VariableType.Date, false, null, "Instante del cambio, en la zona de quien lee."),
                ProductNameVariable,
                AccountKindVariable,
            ],
            ContentVersion: CredentialStateNoticesVersion,
            LayoutKey: "tenant-base"
        );
}
