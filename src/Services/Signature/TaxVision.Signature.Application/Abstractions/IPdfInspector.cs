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
/// Hechos del PDF, sin los límites de la oficina: si es un PDF íntegro (<see cref="StructuralIssues"/> vacío),
/// cuántas páginas tiene y si ya trae firmas. Los límites los aplica <c>DocumentAcceptancePolicy</c>.
/// </summary>
public sealed record PdfInspection(
    IReadOnlyList<DocumentValidationIssue> StructuralIssues,
    string ContentSha256,
    long SizeBytes,
    int? PageCount,
    bool HasExistingSignatures
);

/// <summary>Abre el PDF y extrae sus hechos: MIME, cabecera, integridad, páginas y firmas previas.</summary>
public interface IPdfInspector
{
    PdfInspection Inspect(byte[] content, string declaredContentType);
}
