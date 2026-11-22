using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Texto del push en el idioma del destinatario (sección <c>push</c> del archivo de textos). Genérico a propósito:
/// se ve en la pantalla bloqueada, así que no lleva datos del cliente ni texto escrito por un usuario.
/// </summary>
public static class PushText
{
    /// <summary>Variante de la llamada entrante con video.</summary>
    public const string VideoCall = NotificationKinds.CommunicationCallIncoming + ".video";

    public static (string Title, string Body) For(string key, RecipientLocale locale) =>
        NotificationCopyCatalog.RenderPush(key, locale.Language) is { } text
            ? (text.Title, text.Body)
            : throw new KeyNotFoundException($"No push copy for '{key}'.");
}
