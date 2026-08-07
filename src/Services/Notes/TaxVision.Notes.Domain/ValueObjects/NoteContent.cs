using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Domain.ValueObjects;

/// <summary>
/// Contenido HTML de una nota. <see cref="Html"/> debe llegar YA sanitizado — la sanitización real
/// (<c>Ganss.Xss.HtmlSanitizer</c>) es un puerto de Application/Infrastructure, nunca una
/// dependencia del Domain (guardrail 4: dominio puro, 01_Modelo_De_Dominio.md §2.2). Este VO solo
/// valida "no vacío + longitud máxima" y deriva <see cref="PlainTextPreview"/> con un strip de
/// tags puramente para listados/búsqueda (no reemplaza la sanitización de seguridad ya aplicada
/// antes de llegar aquí).
/// </summary>
public sealed partial record NoteContent
{
    public const int MaxHtmlLength = 102_400; // 100 KB
    public const int PreviewLength = 280;

    public string Html { get; }
    public string PlainTextPreview { get; }

    private NoteContent(string html, string plainTextPreview)
    {
        Html = html;
        PlainTextPreview = plainTextPreview;
    }

    public static Result<NoteContent> Create(string sanitizedHtml)
    {
        if (string.IsNullOrWhiteSpace(sanitizedHtml))
            return Result.Failure<NoteContent>(NoteErrors.ContentEmpty);

        var trimmed = sanitizedHtml.Trim();
        if (trimmed.Length > MaxHtmlLength)
            return Result.Failure<NoteContent>(NoteErrors.ContentTooLong);

        var plainText = StripTags(trimmed);
        if (string.IsNullOrWhiteSpace(plainText))
            return Result.Failure<NoteContent>(NoteErrors.ContentEmpty);

        var preview = plainText.Length > PreviewLength ? plainText[..PreviewLength] : plainText;
        return Result.Success(new NoteContent(trimmed, preview));
    }

    private static string StripTags(string html)
    {
        var withoutTags = TagRegex().Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
