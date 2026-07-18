using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Validation;

/// <summary>
/// Preflight PDF con PdfSharp. Aplica las reglas P-04, P-05 y P-06 del diseño:
/// <list type="bullet">
///   <item>Whitelist de MIME (<c>application/pdf</c>).</item>
///   <item>Tamaño máximo (25 MB por defecto — puede parametrizarse a futuro por tenant).</item>
///   <item>Integridad estructural (PdfSharp abre el archivo sin excepciones).</item>
///   <item>Rango de páginas (1–200 por defecto).</item>
///   <item>Detección de firmas previas — busca <c>AcroForm.SigFlags</c> o campos
///     <c>Sig</c>/<c>FT</c> en <c>AcroForm.Fields</c>. Rechaza para no invalidar la
///     firma anterior cuando estampemos encima.</item>
/// </list>
/// </summary>
public sealed class PdfSharpDocumentValidator : IDocumentValidator
{
    public const long MaxSizeBytes = 25L * 1024 * 1024;
    public const int MaxPageCount = 200;
    public const string PdfMimeType = "application/pdf";

    public DocumentValidationOutcome Validate(byte[] content, string fileName, string declaredContentType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(declaredContentType);

        var sha = ComputeSha256(content);
        var issues = new List<DocumentValidationIssue>();

        var mimeOk = CheckMime(declaredContentType, issues);
        var sizeOk = CheckSize(content.LongLength, issues);
        var magicOk = mimeOk && sizeOk && CheckMagicHeader(content, issues);
        var pdfProbe = magicOk ? TryProbePdf(content, issues) : null;

        return new DocumentValidationOutcome(
            IsAcceptable: issues.Count == 0,
            Issues: issues,
            ContentSha256: sha,
            SizeBytes: content.LongLength,
            PageCount: pdfProbe?.PageCount,
            HasExistingSignatures: pdfProbe?.HasExistingSignatures ?? false
        );
    }

    // ------------------------------------------------------------------
    // Métodos privados: una regla concreta por método
    // ------------------------------------------------------------------

    private sealed record PdfProbeResult(int PageCount, bool HasExistingSignatures);

    private static bool CheckMime(string declaredContentType, List<DocumentValidationIssue> issues)
    {
        if (!string.Equals(declaredContentType.Trim(), PdfMimeType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                new DocumentValidationIssue("Signature.DocumentValidation.MimeType", $"Only {PdfMimeType} is accepted.")
            );
            return false;
        }
        return true;
    }

    private static bool CheckSize(long size, List<DocumentValidationIssue> issues)
    {
        if (size <= 0)
        {
            issues.Add(new DocumentValidationIssue("Signature.DocumentValidation.Empty", "Document content is empty."));
            return false;
        }
        if (size > MaxSizeBytes)
        {
            issues.Add(
                new DocumentValidationIssue(
                    "Signature.DocumentValidation.SizeTooLarge",
                    $"Document exceeds the {MaxSizeBytes / (1024 * 1024)} MB limit."
                )
            );
            return false;
        }
        return true;
    }

    private static bool CheckMagicHeader(byte[] content, List<DocumentValidationIssue> issues)
    {
        // "%PDF-" en ASCII
        var isPdf =
            content.Length >= 5
            && content[0] == 0x25
            && content[1] == 0x50
            && content[2] == 0x44
            && content[3] == 0x46
            && content[4] == 0x2D;
        if (!isPdf)
        {
            issues.Add(
                new DocumentValidationIssue(
                    "Signature.DocumentValidation.MagicHeader",
                    "File does not appear to be a valid PDF (missing %PDF- header)."
                )
            );
            return false;
        }
        return true;
    }

    private static PdfProbeResult? TryProbePdf(byte[] content, List<DocumentValidationIssue> issues)
    {
        try
        {
            using var input = new MemoryStream(content, writable: false);
            using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import);

            var pageCount = pdf.PageCount;
            if (pageCount < 1)
            {
                issues.Add(
                    new DocumentValidationIssue(
                        "Signature.DocumentValidation.NoPages",
                        "PDF must contain at least one page."
                    )
                );
                return new PdfProbeResult(pageCount, false);
            }
            if (pageCount > MaxPageCount)
            {
                issues.Add(
                    new DocumentValidationIssue(
                        "Signature.DocumentValidation.TooManyPages",
                        $"PDF cannot exceed {MaxPageCount} pages."
                    )
                );
            }

            var hasSignatures = HasExistingSignatures(pdf);
            if (hasSignatures)
            {
                issues.Add(
                    new DocumentValidationIssue(
                        "Signature.DocumentValidation.AlreadySigned",
                        "PDF already contains signatures. Re-signing would invalidate the previous signature."
                    )
                );
            }

            return new PdfProbeResult(pageCount, hasSignatures);
        }
        catch (PdfReaderException ex)
        {
            issues.Add(
                new DocumentValidationIssue(
                    "Signature.DocumentValidation.Corrupted",
                    $"PDF integrity check failed: {ex.Message}"
                )
            );
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            issues.Add(
                new DocumentValidationIssue("Signature.DocumentValidation.ProbeFailed", "PDF could not be parsed.")
            );
            return null;
        }
    }

    private static bool HasExistingSignatures(PdfDocument pdf)
    {
        // AcroForm/SigFlags bit 1 (signatures exist) o bit 2 (append-only).
        var acroForm = pdf.Internals.Catalog.Elements.GetDictionary("/AcroForm");
        if (acroForm is null)
            return false;

        var sigFlags = acroForm.Elements.GetInteger("/SigFlags");
        if (sigFlags != 0)
            return true;

        var fields = acroForm.Elements.GetArray("/Fields");
        if (fields is null)
            return false;

        for (var i = 0; i < fields.Elements.Count; i++)
        {
            var field = fields.Elements.GetDictionary(i);
            if (field is null)
                continue;
            var fieldType = field.Elements.GetName("/FT");
            if (string.Equals(fieldType, "/Sig", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
