using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Documents;

namespace TaxVision.Signature.Infrastructure.Validation;

/// <summary>
/// Inspección PDF con PdfSharp (reglas P-04 y P-05 del diseño), sin límites de tenant:
/// <list type="bullet">
///   <item>Whitelist de MIME (<c>application/pdf</c>) y cabecera <c>%PDF-</c>.</item>
///   <item>Integridad estructural (PdfSharp abre el archivo sin excepciones) y al menos una página.</item>
///   <item>Número de páginas y detección de firmas previas — busca <c>AcroForm.SigFlags</c> o campos
///     <c>Sig</c>/<c>FT</c> en <c>AcroForm.Fields</c>.</item>
/// </list>
/// Tamaño y páginas máximas los decide <see cref="DocumentAcceptancePolicy"/> con los límites del tenant.
/// </summary>
public sealed class PdfSharpPdfInspector : IPdfInspector
{
    public PdfInspection Inspect(byte[] content, string declaredContentType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(declaredContentType);

        var issues = new List<DocumentValidationIssue>();
        var probe =
            CheckMime(declaredContentType, issues) && CheckNotEmpty(content, issues) && CheckMagicHeader(content, issues)
                ? TryProbePdf(content, issues)
                : null;

        return new PdfInspection(
            StructuralIssues: issues,
            ContentSha256: ComputeSha256(content),
            SizeBytes: content.LongLength,
            PageCount: probe?.PageCount,
            HasExistingSignatures: probe?.HasExistingSignatures ?? false
        );
    }

    // ------------------------------------------------------------------
    // Métodos privados: una regla concreta por método
    // ------------------------------------------------------------------

    private sealed record PdfProbeResult(int PageCount, bool HasExistingSignatures);

    private static bool CheckMime(string declaredContentType, List<DocumentValidationIssue> issues)
    {
        if (DocumentAcceptancePolicy.IsPdfContentType(declaredContentType))
            return true;
        issues.Add(DocumentAcceptancePolicy.NotAPdf());
        return false;
    }

    private static bool CheckNotEmpty(byte[] content, List<DocumentValidationIssue> issues)
    {
        if (content.LongLength > 0)
            return true;
        issues.Add(new DocumentValidationIssue("Signature.DocumentValidation.Empty", "Document content is empty."));
        return false;
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
                return null;
            }

            return new PdfProbeResult(pageCount, HasExistingSignatures(pdf));
        }
        // Entrada no confiable: PdfSharp también lanza System.Exception genérica (p. ej. sin tabla xref).
        // Cualquier fallo al abrir es un PDF dañado, nunca un 500.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            issues.Add(Corrupted);
            return null;
        }
    }

    private static readonly DocumentValidationIssue Corrupted = new(
        "Signature.DocumentValidation.Corrupted",
        "The PDF is damaged or incomplete. Open it and save it again as a PDF."
    );

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
