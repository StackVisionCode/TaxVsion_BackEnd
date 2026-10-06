using System.Text.RegularExpressions;

namespace BuildingBlocks.Messaging.CampaignsIntegrationEvents;

/// <summary>
/// Sustitución de variables de personalización <c>{{token}}</c> en el contenido de una campaña, por
/// destinatario. Vive en BuildingBlocks para que los TRES ejecutores (Email/Push en Notification, SMS en
/// TaxVision.Sms) y Campaigns lo compartan. Tokens soportados: <c>first_name, last_name, full_name, name,
/// email, phone, phone_number</c>. Un token desconocido se deja literal (no rompe el envío).
/// </summary>
public static partial class CampaignPersonalization
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    /// <summary>Reemplaza los tokens en <paramref name="template"/> con los datos del destinatario.
    /// <paramref name="recipientName"/> es el nombre completo (first = primera palabra, last = el resto).</summary>
    public static string? Render(string? template, string? recipientName, string? email, string? phoneE164)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        var full = (recipientName ?? string.Empty).Trim();
        var space = full.IndexOf(' ');
        var first = space < 0 ? full : full[..space];
        var last = space < 0 ? string.Empty : full[(space + 1)..].Trim();

        return TokenRegex()
            .Replace(
                template,
                m =>
                    m.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "first_name" => first,
                        "last_name" => last,
                        "full_name" or "name" => full,
                        "email" => email ?? string.Empty,
                        "phone" or "phone_number" => phoneE164 ?? string.Empty,
                        _ => m.Value, // token desconocido: se deja tal cual
                    }
            );
    }
}
