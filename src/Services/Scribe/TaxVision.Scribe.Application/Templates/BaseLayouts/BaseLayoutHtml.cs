namespace TaxVision.Scribe.Application.Templates.BaseLayouts;

/// <summary>
/// HTML de los 2 layouts base (Fase 4.6) que todo EmailTemplate DEBE extender: system-base-v1 y
/// tenant-base-v1. Autoría por adelantado, validada contra <see cref="Validation.EmailHtmlSafetyValidator"/>
/// en tests — la siembra real (subir a CloudStorage + insertar EmailLayout/EmailLayoutVersion vía
/// TemplateStorageService) queda para Fase 5, que es dueña del upload. Ambos usan {{ body | raw }}
/// (no {{ body }} a secas) porque FluidTemplateRenderer renderiza el layout completo con Fluid — ver
/// el comentario de clase en FluidTemplateRenderer para el porqué.
/// </summary>
public static class BaseLayoutHtml
{
    public const string SystemBaseV1 = """
        <!DOCTYPE html>
        <html lang="es" xmlns="http://www.w3.org/1999/xhtml">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <!--[if mso]>
        <style type="text/css">
          body, table, td, p, a, li { font-family: Arial, sans-serif !important; }
        </style>
        <![endif]-->
        <style type="text/css">
          @media (max-width: 480px) {
            .mobile-full-width { width: 100% !important; }
            .mobile-padding { padding: 12px !important; }
          }
          @media (prefers-color-scheme: dark) {
            body { background-color: #1a1a1a !important; color: #ffffff !important; }
          }
        </style>
        </head>
        <body style="margin:0;padding:0;background-color:#f4f4f4;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f4f4f4">
          <tr>
            <td align="center" style="padding:24px 12px;">
              <table role="presentation" class="mobile-full-width" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="max-width:600px;width:100%;">
                <tr>
                  <td align="center" class="mobile-padding" style="padding:24px;">
                    <img src="cid:logo-header" width="180" height="auto" alt="TaxVision" style="display:block;">
                  </td>
                </tr>
                <tr>
                  <td class="mobile-padding" style="padding:0 24px 24px 24px;font-family:Arial,sans-serif;font-size:14px;color:#333333;">
                    {{ body | raw }}
                  </td>
                </tr>
                <tr>
                  <td align="center" style="padding:16px 24px;background-color:#f4f4f4;font-family:Arial,sans-serif;font-size:11px;color:#888888;">
                    Este es un correo automático de TaxVision. No responder. &copy; {{ current_year }} TaxVision.
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
        <html lang="es" xmlns="http://www.w3.org/1999/xhtml">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <!--[if mso]>
        <style type="text/css">
          body, table, td, p, a, li { font-family: Arial, sans-serif !important; }
        </style>
        <![endif]-->
        <style type="text/css">
          @media (max-width: 480px) {
            .mobile-full-width { width: 100% !important; }
            .mobile-padding { padding: 12px !important; }
          }
          @media (prefers-color-scheme: dark) {
            body { background-color: #1a1a1a !important; color: #ffffff !important; }
          }
        </style>
        </head>
        <body style="margin:0;padding:0;background-color:#f4f4f4;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f4f4f4">
          <tr>
            <td align="center" style="padding:24px 12px;">
              <table role="presentation" class="mobile-full-width" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="max-width:600px;width:100%;">
                <tr>
                  <td align="center" class="mobile-padding" style="padding:24px;">
                    <img src="cid:logo-header" width="180" height="auto" alt="{{ tenant_name }}" style="display:block;">
                  </td>
                </tr>
                {% if tenant_logo_missing %}
                <tr>
                  <td style="padding:0;">
                    <table role="presentation" width="100%" bgcolor="#fff4e5">
                      <tr>
                        <td style="padding:8px 16px;font-size:12px;color:#8a5a00;font-family:Arial,sans-serif;">
                          Configura tu logo en Ajustes &rarr; Branding para personalizar este correo.
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
                {% endif %}
                <tr>
                  <td class="mobile-padding" style="padding:0 24px 24px 24px;font-family:Arial,sans-serif;font-size:14px;color:#333333;">
                    {{ body | raw }}
                  </td>
                </tr>
                <tr>
                  <td align="center" style="padding:16px 24px;background-color:#f4f4f4;font-family:Arial,sans-serif;font-size:11px;color:#888888;">
                    {{ tenant_name }} &middot; {{ tenant_address }} &middot; Enviado desde TaxVision
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
