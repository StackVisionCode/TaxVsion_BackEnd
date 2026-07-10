namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Regla concreta que produjo el rechazo (para el frontend + el log).</summary>
public sealed record DocumentValidationIssue(string Code, string Message);

/// <summary>
/// Resultado del preflight: si <see cref="IsAcceptable"/> es <c>false</c>, la lista de
/// <see cref="Issues"/> explica cada regla incumplida. Contiene también las métricas
/// que Signature guardará en <c>DocumentValidationRecord</c>.
/// </summary>
public sealed record DocumentValidationOutcome(
    bool IsAcceptable,
    IReadOnlyList<DocumentValidationIssue> Issues,
    string ContentSha256,
    long SizeBytes,
    int? PageCount,
    bool HasExistingSignatures
);

/// <summary>
/// Ejecuta el preflight PDF: MIME whitelist, tamaño, integridad estructural,
/// número de páginas y detección de firmas previas (evita re-firmar un PDF ya firmado
/// y romperle la firma anterior — regla P-05 del diseño).
/// </summary>
public interface IDocumentValidator
{
    DocumentValidationOutcome Validate(byte[] content, string fileName, string declaredContentType);
}
