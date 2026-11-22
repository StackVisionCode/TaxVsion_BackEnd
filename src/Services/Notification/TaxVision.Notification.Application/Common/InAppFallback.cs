using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Respaldo del in-app (<c>FallbackTitle</c>/<c>FallbackBody</c>): el texto inglés del archivo de textos con los
/// parámetros puestos. El frontend traduce por kind; esto solo se ve donde no hay traducción.
/// </summary>
public static class InAppFallback
{
    public static (string Title, string Body) For(
        string kind,
        IReadOnlyDictionary<string, string>? parameters = null
    ) =>
        NotificationCopyCatalog.Render(kind, NotificationCopyCatalog.DefaultLanguage, parameters) is { } text
            ? (text.Title, text.Body)
            : throw new KeyNotFoundException($"No copy for '{kind}'.");
}
