using BuildingBlocks.Results;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Datos que se leen del trailer de un PDF ya generado por PdfSharp: offset del xref
/// vigente, tamano de la tabla y ultimo <c>%%EOF</c>. Se necesita para construir un
/// incremental update legal.
/// </summary>
public readonly record struct PdfTrailerInfo(long StartXref, long PrevSize, long EofOffset);

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

        return Result.Success(new PdfTrailerInfo(startXrefResult.Value, sizeResult.Value, eofOffset));
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
}
