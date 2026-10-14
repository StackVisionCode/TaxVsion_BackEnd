namespace TaxVision.Scribe.Application.Templates.BaseLayouts;

/// <summary>
/// HTML de los 2 layouts base que todo EmailTemplate DEBE extender: system-base-v1 y tenant-base-v1.
/// La cáscara (barra superior, card redondeada, wordmark/logo, footer) vive acá; cada template solo
/// aporta el contenido que entra en {{ body | raw }} (eyebrow + título + cuerpo + botón). Ambos usan
/// {{ body | raw }} (no {{ body }} a secas) porque FluidTemplateRenderer renderiza el layout completo
/// con Fluid. El renderer inyecta subject/current_year/tenant_logo_missing y la marca de la plataforma
/// (product_name, platform_legal_name: salen de PlatformSettings, nunca escritas acá); tenant_name/
/// tenant_address llegan como variables de runtime. Paleta Taxoffix (crema/Twilight), forzada a light, compatible Outlook/MSO + móvil.
/// </summary>
public static class BaseLayoutHtml
{
    // Subir esto cuando cambie el HTML del layout: el seeder republica si supera al guardado.
    // v11: la marca sale de product_name/platform_legal_name en vez de "TaxProffice" escrito.
    // v13: diseño editorial (sin tarjeta): fondo crema, logo + línea Twilight, texto en gris azulado, paleta Taxoffix.
    // v14: fuera el wordmark de texto al lado del logo — el PNG del sistema ya trae la marca dibujada, así que
    //      el texto la duplicaba. El header ahora es solo el <img> con el nombre en `alt=` (lectores de pantalla).
    // v15: el <img> del logo se adapta a cualquier proporción: altura fija (44px) + width:auto + max-width del
    //      contenedor; en Outlook antiguo (sin CSS) los atributos (260x44) matchean el logo horizontal del sistema.
    public const int SystemBaseVersion = 15;

    // v7: fuera el aviso "Set your logo in Settings -> Branding". tenant-base lo lee el CLIENTE de la
    // oficina, no su personal; con logo o sin logo el correo no habla de la configuracion interna.
    // v8: "Sent from {{ product_name }}" en vez de la marca escrita.
    // v10: mismo diseño editorial que system-base (paleta Taxoffix), con el logo/nombre de la oficina.
    // v11: logo del tenant adaptativo (altura fija + ancho auto): cada oficina sube uno con otra proporción.
    public const int TenantBaseVersion = 11;

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
        <title>{{ product_name }}</title>
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
        <body bgcolor="#F8F4ED" style="margin:0;padding:0;background-color:#F8F4ED;">
        <div style="display:none;font-size:1px;color:#F8F4ED;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{{ preheader }}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#F8F4ED" style="background-color:#F8F4ED;">
          <tr>
            <td align="center" class="mobile-shell" style="padding:40px 12px;">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" class="mobile-full-width" style="width:600px;max-width:600px;">
                <tr>
                  <td class="mobile-padding" style="padding:0 40px 24px 40px;">
                    <img src="cid:logo-header" width="260" height="44" alt="{{ product_name }}" style="display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;height:44px;width:auto;max-width:320px;">
                  </td>
                </tr>
                <tr>
                  <td height="1" bgcolor="#083645" style="background-color:#083645;height:1px;line-height:1px;font-size:0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" style="padding:36px 40px 36px 40px;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:26px;color:#3E5F6B;">
                    {{ body | raw }}
                  </td>
                </tr>
                <tr>
                  <td height="1" bgcolor="#E4DCCF" style="background-color:#E4DCCF;height:1px;line-height:1px;font-size:0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" style="padding:22px 40px 6px 40px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#3E5F6B;mso-line-height-rule:exactly;">
                    This is an automated message from <strong style="color:#083645;">{{ product_name }}</strong>. Please do not reply to this address.
                  </td>
                </tr>
                <tr>
                  <td class="mobile-padding" style="padding:6px 40px 0 40px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:17px;color:#3E5F6B;mso-line-height-rule:exactly;">
                    &copy; {{ current_year }} {{ platform_legal_name }} &middot; All rights reserved
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
        <body bgcolor="#F8F4ED" style="margin:0;padding:0;background-color:#F8F4ED;">
        <div style="display:none;font-size:1px;color:#F8F4ED;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{{ preheader }}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#F8F4ED" style="background-color:#F8F4ED;">
          <tr>
            <td align="center" class="mobile-shell" style="padding:40px 12px;">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" class="mobile-full-width" style="width:600px;max-width:600px;">
                <tr>
                  <td class="mobile-padding" style="padding:0 40px 24px 40px;">
                    {% if tenant_logo_missing %}
                    <div style="font-family:Arial,Helvetica,sans-serif;font-size:20px;line-height:26px;font-weight:bold;letter-spacing:-0.4px;color:#083645;">{{ tenant_name }}</div>
                    {% else %}
                    <img src="cid:logo-header" width="160" height="44" alt="{{ tenant_name }}" style="display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;height:44px;width:auto;max-width:320px;">
                    {% endif %}
                  </td>
                </tr>
                <tr>
                  <td height="1" bgcolor="#083645" style="background-color:#083645;height:1px;line-height:1px;font-size:0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" style="padding:36px 40px 36px 40px;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:26px;color:#3E5F6B;">
                    {{ body | raw }}
                  </td>
                </tr>
                <tr>
                  <td height="1" bgcolor="#E4DCCF" style="background-color:#E4DCCF;height:1px;line-height:1px;font-size:0;">&nbsp;</td>
                </tr>
                <tr>
                  <td class="mobile-padding" style="padding:22px 40px 6px 40px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:19px;color:#3E5F6B;mso-line-height-rule:exactly;">
                    <strong style="color:#083645;">{{ tenant_name }}</strong>{% if tenant_address != blank %} &middot; {{ tenant_address }}{% endif %}<br />Sent from {{ product_name }}
                  </td>
                </tr>
                <tr>
                  <td class="mobile-padding" style="padding:6px 40px 0 40px;font-family:Arial,Helvetica,sans-serif;font-size:11px;line-height:17px;color:#3E5F6B;mso-line-height-rule:exactly;">
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
