using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Texto que el sistema mete dentro de un correo (sección <c>email</c> del archivo de textos), en el idioma de quien
/// lee. Lo que escribió una persona viaja tal cual y no pasa por aquí.
/// </summary>
public static class EmailText
{
    public static string For(
        string key,
        RecipientLocale locale,
        IReadOnlyDictionary<string, string>? parameters = null
    ) => For(key, locale.Language, parameters);

    /// <summary>Con el idioma en dos letras (<c>en</c>/<c>es</c>); lo desconocido cae a inglés.</summary>
    public static string For(string key, string? language, IReadOnlyDictionary<string, string>? parameters = null) =>
        NotificationCopyCatalog.RenderEmail(key, language, parameters)
        ?? throw new KeyNotFoundException($"No email copy for '{key}'.");
}
