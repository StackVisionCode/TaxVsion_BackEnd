using System.Globalization;
using System.Text;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Nombre de descarga de los PDF de una solicitud (original, sellado, certificado, copia parcial) a partir
/// del título: quita la extensión .pdf que el título suele heredar del archivo subido, translitera acentos
/// y deja solo [A-Za-z0-9._-]. No toca el ObjectKey.
/// </summary>
public static class DocumentFileName
{
    public const int MaxBaseLength = 120;
    private const string PdfExtension = ".pdf";
    private const string Fallback = "Document";

    /// <summary>Título limpio + sufijo (p. ej. "_Signed.pdf", ".pdf").</summary>
    public static string For(string? title, string suffix) => Base(title) + suffix;

    /// <summary>Solo la parte limpia, para componer nombres de varias partes.</summary>
    public static string Base(string? title)
    {
        var baseName = (title ?? string.Empty).Trim();
        if (baseName.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^PdfExtension.Length];

        var normalized = baseName.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasUnderscore = false;
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.')
            {
                sb.Append(ch);
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }

        var cleaned = sb.ToString().Trim('_', '.', '-');
        if (cleaned.Length > MaxBaseLength)
            cleaned = cleaned[..MaxBaseLength].Trim('_', '.', '-');
        return string.IsNullOrEmpty(cleaned) ? Fallback : cleaned;
    }
}
