using BuildingBlocks.Results;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Datos que se leen del trailer de un PDF ya generado por PdfSharp: offset del xref
/// vigente, tamano de la tabla, ultimo <c>%%EOF</c>, y — clave para no corromper el PDF —
/// el numero de objeto del <c>/Root</c> (Catalog) original y sus claves. El incremental
/// update de firma NO debe acunar un Catalog nuevo: debe REDEFINIR el original conservando
/// <c>/Pages</c> (obligatorio, PDF 32000-1 §7.7.2) y agregarle <c>/AcroForm</c>.
/// </summary>
public readonly record struct PdfTrailerInfo(
    long StartXref,
    long PrevSize,
    long EofOffset,
    int RootObjectNumber,
    string RootCatalogKeys
);

/// <summary>
/// Localiza el ultimo <c>startxref</c>, el <c>/Size</c> del trailer y la posicion del
/// ultimo <c>%%EOF</c> de un PDF. Solo lee los ultimos ~4 KB — el trailer siempre esta
/// al final por especificacion PDF 32000-1 (§7.5.5).
/// </summary>
public static class PdfTrailerParser
{
    private const int TailWindowBytes = 4 * 1024;

    public static Result<PdfTrailerInfo> Parse(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length < 32)
            return Result.Failure<PdfTrailerInfo>(
                new Error("Signature.PadesB.PdfTooSmall", "PDF too small to contain a trailer.")
            );

        var window = ReadTailWindow(pdfBytes);
        var eofOffset = LocateLastEof(pdfBytes, window);
        if (eofOffset < 0)
            return Result.Failure<PdfTrailerInfo>(
                new Error("Signature.PadesB.MissingEof", "No %%EOF marker found near end of PDF.")
            );

        var startXrefResult = LocateStartXref(pdfBytes, window);
        if (startXrefResult.IsFailure)
            return Result.Failure<PdfTrailerInfo>(startXrefResult.Error);

        var sizeResult = LocateTrailerSize(pdfBytes, window);
        if (sizeResult.IsFailure)
            return Result.Failure<PdfTrailerInfo>(sizeResult.Error);

        var rootObj = LocateRootObjectNumber(window);
        if (rootObj is null)
            return Result.Failure<PdfTrailerInfo>(
                new Error("Signature.PadesB.MissingRoot", "No /Root reference found in the PDF trailer.")
            );

        var catalogKeys = ExtractObjectDictInner(pdfBytes, rootObj.Value);
        if (catalogKeys is null)
            return Result.Failure<PdfTrailerInfo>(
                new Error("Signature.PadesB.MissingCatalog", "The /Root Catalog object could not be read.")
            );

        return Result.Success(
            new PdfTrailerInfo(startXrefResult.Value, sizeResult.Value, eofOffset, rootObj.Value, catalogKeys)
        );
    }

    // ------------------------------------------------------------------

    private static string ReadTailWindow(byte[] pdf)
    {
        var start = Math.Max(0, pdf.Length - TailWindowBytes);
        return System.Text.Encoding.Latin1.GetString(pdf, start, pdf.Length - start);
    }

    private static long LocateLastEof(byte[] pdf, string window)
    {
        var idx = window.LastIndexOf("%%EOF", StringComparison.Ordinal);
        if (idx < 0)
            return -1;
        var absolute = pdf.Length - window.Length + idx;
        // Avanzamos hasta el primer byte tras EOF incluyendo su newline final si existe.
        var afterEof = absolute + 5;
        if (afterEof < pdf.Length && pdf[afterEof] is (byte)'\r' or (byte)'\n')
            afterEof++;
        if (afterEof < pdf.Length && pdf[afterEof] == '\n')
            afterEof++;
        return afterEof;
    }

    private static Result<long> LocateStartXref(byte[] pdf, string window)
    {
        var idx = window.LastIndexOf("startxref", StringComparison.Ordinal);
        if (idx < 0)
            return Result.Failure<long>(new Error("Signature.PadesB.MissingStartxref", "No startxref marker."));

        var rest = window[(idx + "startxref".Length)..];
        var digits = ExtractFirstInteger(rest);
        if (digits is null || !long.TryParse(digits, out var offset))
            return Result.Failure<long>(new Error("Signature.PadesB.InvalidStartxref", "startxref offset unreadable."));
        return Result.Success(offset);
    }

    private static Result<long> LocateTrailerSize(byte[] pdf, string window)
    {
        var idx = window.LastIndexOf("/Size", StringComparison.Ordinal);
        if (idx < 0)
            return Result.Success(0L); // xref stream (PDF 1.5+) no siempre tiene trailer text; toleramos.

        var rest = window[(idx + "/Size".Length)..];
        var digits = ExtractFirstInteger(rest);
        if (digits is null || !long.TryParse(digits, out var size))
            return Result.Success(0L);
        return Result.Success(size);
    }

    private static string? ExtractFirstInteger(string source)
    {
        var start = -1;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (start < 0 && char.IsDigit(c))
            {
                start = i;
                continue;
            }
            if (start >= 0 && !char.IsDigit(c))
                return source[start..i];
        }
        return start >= 0 ? source[start..] : null;
    }

    private static int? LocateRootObjectNumber(string window)
    {
        var idx = window.LastIndexOf("/Root", StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var num = ExtractFirstInteger(window[(idx + "/Root".Length)..]);
        return num is not null && int.TryParse(num, out var n) ? n : null;
    }

    /// <summary>
    /// Devuelve el contenido interno (entre <c>&lt;&lt;</c> y <c>&gt;&gt;</c>) del diccionario del objeto
    /// dado. Latin1 mapea 1 byte = 1 char, así que los índices del string coinciden con offsets de byte.
    /// </summary>
    private static string? ExtractObjectDictInner(byte[] pdf, int objectNumber)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var marker = $"{objectNumber} 0 obj";
        var objIdx = FindObjectDefinition(text, marker);
        if (objIdx < 0)
            return null;

        var dictStart = text.IndexOf("<<", objIdx + marker.Length, StringComparison.Ordinal);
        if (dictStart < 0)
            return null;

        var depth = 0;
        for (var i = dictStart; i < text.Length - 1; i++)
        {
            if (text[i] == '<' && text[i + 1] == '<')
            {
                depth++;
                i++;
                continue;
            }
            if (text[i] == '>' && text[i + 1] == '>')
            {
                depth--;
                i++;
                if (depth == 0)
                    return StripAcroForm(text[(dictStart + 2)..(i - 1)]).Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// Localiza <c>"{n} 0 obj"</c> cuyo char previo NO sea dígito, para no matchear <c>"2 0 obj"</c>
    /// dentro de <c>"12 0 obj"</c>. Toma la última definición (revisión más reciente).
    /// </summary>
    private static int FindObjectDefinition(string text, string marker)
    {
        var from = text.Length;
        while (from > 0)
        {
            var idx = text.LastIndexOf(marker, from - 1, StringComparison.Ordinal);
            if (idx < 0)
                return -1;
            if (idx == 0 || !char.IsDigit(text[idx - 1]))
                return idx;
            from = idx;
        }
        return -1;
    }

    /// <summary>
    /// Defensivo: un Catalog recién serializado por PdfSharp no trae <c>/AcroForm</c>, pero si lo
    /// trajera lo quitamos para no duplicarlo cuando el appender agregue el suyo.
    /// </summary>
    private static string StripAcroForm(string dictInner)
    {
        var idx = dictInner.IndexOf("/AcroForm", StringComparison.Ordinal);
        if (idx < 0)
            return dictInner;
        var rest = dictInner[(idx + "/AcroForm".Length)..];
        var match = System.Text.RegularExpressions.Regex.Match(rest, @"^\s+\d+\s+0\s+R");
        var end = idx + "/AcroForm".Length + (match.Success ? match.Length : 0);
        return dictInner[..idx] + dictInner[end..];
    }
}
