using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Documents;

/// <summary>
/// Reglas de aceptación de un PDF para firmar a partir de sus hechos (tamaño, páginas, firmas previas) y de
/// los límites del tenant. Una sola regla para el preflight del navegador y para la verificación del servidor.
/// </summary>
public static class DocumentAcceptancePolicy
{
    public const string PdfMimeType = "application/pdf";

    public static IReadOnlyList<DocumentValidationIssue> Evaluate(
        long sizeBytes,
        int? pageCount,
        bool hasExistingSignatures,
        DocumentLimits limits
    )
    {
        ArgumentNullException.ThrowIfNull(limits);
        var issues = new List<DocumentValidationIssue>();

        if (sizeBytes > limits.MaxPdfBytes)
            issues.Add(SizeTooLarge(limits));
        if (pageCount > limits.MaxPagesPerDocument)
            issues.Add(
                new DocumentValidationIssue(
                    "Signature.DocumentValidation.TooManyPages",
                    $"PDF cannot exceed {limits.MaxPagesPerDocument} pages."
                )
            );
        if (hasExistingSignatures)
            issues.Add(
                new DocumentValidationIssue(
                    "Signature.DocumentValidation.AlreadySigned",
                    "PDF already contains signatures. Re-signing would invalidate the previous signature."
                )
            );

        return issues;
    }

    /// <summary>Preflight: hechos del PDF + límites del tenant.</summary>
    public static DocumentValidationOutcome Combine(PdfInspection inspection, DocumentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var issues = inspection
            .StructuralIssues.Concat(
                Evaluate(inspection.SizeBytes, inspection.PageCount, inspection.HasExistingSignatures, limits)
            )
            .ToList();

        return new DocumentValidationOutcome(
            IsAcceptable: issues.Count == 0,
            Issues: issues,
            ContentSha256: inspection.ContentSha256,
            SizeBytes: inspection.SizeBytes,
            PageCount: inspection.PageCount,
            HasExistingSignatures: inspection.HasExistingSignatures
        );
    }

    public static DocumentValidationIssue SizeTooLarge(DocumentLimits limits) =>
        new(
            "Signature.DocumentValidation.SizeTooLarge",
            $"Document exceeds the {FormatMegabytes(limits.MaxPdfBytes)} MB limit."
        );

    public static DocumentValidationIssue NotAPdf() =>
        new("Signature.DocumentValidation.MimeType", $"Only {PdfMimeType} is accepted.");

    public static bool IsPdfContentType(string? contentType) =>
        string.Equals(contentType?.Split(';')[0].Trim(), PdfMimeType, StringComparison.OrdinalIgnoreCase);

    private static string FormatMegabytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
