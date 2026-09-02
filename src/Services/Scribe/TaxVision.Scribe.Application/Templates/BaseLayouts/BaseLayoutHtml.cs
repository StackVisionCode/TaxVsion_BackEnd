namespace TaxVision.Scribe.Application.Templates.BaseLayouts;

/// <summary>
/// HTML de los 2 layouts base que todo EmailTemplate DEBE extender: system-base-v1 y tenant-base-v1.
/// La cáscara (barra superior, card redondeada, wordmark/logo, footer) vive acá; cada template solo
/// aporta el contenido que entra en {{ body | raw }} (eyebrow + título + cuerpo + botón). Ambos usan
/// {{ body | raw }} (no {{ body }} a secas) porque FluidTemplateRenderer renderiza el layout completo
/// con Fluid. El renderer inyecta subject/current_year/tenant_logo_missing; tenant_name/tenant_address
/// llegan como variables de runtime. Paleta Bold Blue, forzada a light, compatible Outlook/MSO + móvil.
/// </summary>
public static class BaseLayoutHtml
{
    // Subir esto cuando cambie el HTML del layout: el seeder republica si supera al guardado.
    public const int SystemBaseVersion = 7;
    public const int TenantBaseVersion = 5;

    public const string SystemBaseV1 = """
        <!DOCTYPE html>
        <html lang="en" xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
        <head>
        <meta charset="utf-8">
        <meta http-equiv="X-UA-Compatible" content="IE=edge">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <meta name="format-detection" content="telephone=no,date=no,address=no,email=no">
        <meta name="color-scheme" content="light">
        <meta name="supported-color-schemes" content="light">
        <title>TaxProffice</title>
        <!--[if mso]>
        <xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml>
        <style type="text/css">
          body, table, td, p, a, li, blockquote { font-family: Arial, Helvetica, sans-serif !important; }
          table { border-collapse: collapse !important; }
        </style>
        <![endif]-->
        <style type="text/css">
          html, body { margin:0 !important; padding:0 !important; width:100% !important; }
          body { -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
          table { border-collapse:collapse; mso-table-lspace:0pt; mso-table-rspace:0pt; }
          img { border:0; outline:none; text-decoration:none; -ms-interpolation-mode:bicubic; }
          a { text-decoration:none; }
          @media only screen and (max-width: 600px) {
            .mobile-shell { padding-left:8px !important; padding-right:8px !important; }
            .mobile-full-width { width:100% !important; max-width:100% !important; }
            .mobile-padding { padding-left:22px !important; padding-right:22px !important; }
            img { max-width:100% !important; height:auto !important; }
          }
        </style>
        </head>
        <body bgcolor="#F7FBFE" style="margin:0;padding:0;background-color:#F7FBFE;">
        <div style="display:none;font-size:1px;color:#F7FBFE;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{{ preheader }}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#F7FBFE" style="background-color:#F7FBFE;">
          <tr>
            <td align="center" class="mobile-shell" style="padding:32px 12px;">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" class="mobile-full-width" style="width:600px;max-width:600px;">
                <tr>
                  <td height="6" bgcolor="#67BAF4" style="background-color:#67BAF4;height:6px;line-height:6px;font-size:0;border-radius:14px 14px 0 0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" bgcolor="#FFFFFF" style="background-color:#FFFFFF;padding:26px 40px 8px 40px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                      <tr>
                        <td valign="middle" style="padding-right:11px;">
                          <img src="cid:logo-header" width="40" height="40" alt="TaxProffice" style="display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;width:40px;height:40px;">
                        </td>
                        <td valign="middle" style="font-family:Arial,Helvetica,sans-serif;font-size:20px;line-height:24px;font-weight:bold;letter-spacing:-0.2px;color:#1E466B;mso-line-height-rule:exactly;">Tax<span style="color:#67BAF4;">Proffice</span></td>
                      </tr>
                    </table>
                  </td>
                </tr>
                <tr>
                  <td class="mobile-padding" bgcolor="#FFFFFF" style="background-color:#FFFFFF;padding:8px 40px 30px 40px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">
                    {{ body | raw }}
                  </td>
                </tr>
                <tr>
                  <td height="1" bgcolor="#DCECF7" style="background-color:#DCECF7;height:1px;line-height:1px;font-size:0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" bgcolor="#FFFFFF" style="background-color:#FFFFFF;padding:22px 40px 26px 40px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#70869A;mso-line-height-rule:exactly;border-radius:0 0 14px 14px;">
                    This is an automated message from <strong style="color:#1E466B;">TaxProffice</strong>. Please do not reply to this address.
                  </td>
                </tr>
                <tr>
                  <td align="center" style="padding:18px 16px 4px 16px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:17px;color:#70869A;mso-line-height-rule:exactly;">
                    &copy; {{ current_year }} TaxProffice &middot; All rights reserved
                  </td>
                </tr>
              </table>
            </td>
          </tr>
        </table>
        </body>
        </html>
        """;

    public const string TenantBaseV1 = """
        <!DOCTYPE html>
        <html lang="en" xmlns="http://www.w3.org/1999/xhtml" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
        <head>
        <meta charset="utf-8">
        <meta http-equiv="X-UA-Compatible" content="IE=edge">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <meta name="format-detection" content="telephone=no,date=no,address=no,email=no">
        <meta name="color-scheme" content="light">
        <meta name="supported-color-schemes" content="light">
        <title>{{ tenant_name }}</title>
        <!--[if mso]>
        <xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml>
        <style type="text/css">
          body, table, td, p, a, li, blockquote { font-family: Arial, Helvetica, sans-serif !important; }
          table { border-collapse: collapse !important; }
        </style>
        <![endif]-->
        <style type="text/css">
          html, body { margin:0 !important; padding:0 !important; width:100% !important; }
          body { -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }
          table { border-collapse:collapse; mso-table-lspace:0pt; mso-table-rspace:0pt; }
          img { border:0; outline:none; text-decoration:none; -ms-interpolation-mode:bicubic; }
          a { text-decoration:none; }
          @media only screen and (max-width: 600px) {
            .mobile-shell { padding-left:8px !important; padding-right:8px !important; }
            .mobile-full-width { width:100% !important; max-width:100% !important; }
            .mobile-padding { padding-left:22px !important; padding-right:22px !important; }
            img { max-width:100% !important; height:auto !important; }
          }
        </style>
        </head>
        <body bgcolor="#F7FBFE" style="margin:0;padding:0;background-color:#F7FBFE;">
        <div style="display:none;font-size:1px;color:#F7FBFE;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{{ preheader }}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#F7FBFE" style="background-color:#F7FBFE;">
          <tr>
            <td align="center" class="mobile-shell" style="padding:32px 12px;">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" class="mobile-full-width" style="width:600px;max-width:600px;">
                <tr>
                  <td height="6" bgcolor="#67BAF4" style="background-color:#67BAF4;height:6px;line-height:6px;font-size:0;border-radius:14px 14px 0 0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" bgcolor="#FFFFFF" style="background-color:#FFFFFF;padding:26px 40px 4px 40px;">
                    {% if tenant_logo_missing %}
                    <div style="font-family:Arial,Helvetica,sans-serif;font-size:19px;line-height:24px;font-weight:bold;letter-spacing:-0.2px;color:#1E466B;">{{ tenant_name }}</div>
                    {% else %}
                    <img src="cid:logo-header" width="160" height="40" alt="{{ tenant_name }}" style="display:block;max-height:44px;">
                    {% endif %}
                  </td>
                </tr>
                {% if tenant_logo_missing %}
                <tr>
                  <td class="mobile-padding" bgcolor="#FFFFFF" style="background-color:#FFFFFF;padding:8px 40px 0 40px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#EAF4FF" style="background-color:#EAF4FF;border-radius:10px;">
                      <tr>
                        <td style="padding:10px 16px;border-left:3px solid #67BAF4;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#496174;mso-line-height-rule:exactly;">
                          Set your logo in <strong style="color:#1E466B;">Settings &rarr; Branding</strong> to personalize this email.
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
                {% endif %}
                <tr>
                  <td class="mobile-padding" bgcolor="#FFFFFF" style="background-color:#FFFFFF;padding:14px 40px 30px 40px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:24px;color:#496174;">
                    {{ body | raw }}
                  </td>
                </tr>
                <tr>
                  <td height="1" bgcolor="#DCECF7" style="background-color:#DCECF7;height:1px;line-height:1px;font-size:0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" bgcolor="#FFFFFF" style="background-color:#FFFFFF;padding:22px 40px 26px 40px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:18px;color:#70869A;mso-line-height-rule:exactly;border-radius:0 0 14px 14px;">
                    <strong style="color:#1E466B;">{{ tenant_name }}</strong>{% if tenant_address != blank %} &middot; {{ tenant_address }}{% endif %}<br />Sent from TaxProffice
                  </td>
                </tr>
                <tr>
                  <td align="center" style="padding:18px 16px 4px 16px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:17px;color:#70869A;mso-line-height-rule:exactly;">
                    &copy; {{ current_year }} {{ tenant_name }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>
        </table>
        </body>
        </html>
        """;
}
