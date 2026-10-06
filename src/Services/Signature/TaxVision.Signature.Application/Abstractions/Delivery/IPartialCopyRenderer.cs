using TaxVision.Signature.Application.Abstractions.Sealing;

namespace TaxVision.Signature.Application.Abstractions.Delivery;

/// <summary>
/// F7 — rendea el PDF decorado que recibe un firmante al terminar su firma. No es un sellado
/// legal: sin PAdES, sin TSA, sin DSS. Solo estampa las firmas ya recogidas y pone marca de
/// agua + pie con progreso. Puro: sin I/O, sin acceso a dominio.
/// </summary>
public interface IPartialCopyRenderer
{
    PartialCopyResult Render(PartialCopyRequest request);
}

public sealed record PartialCopyRequest(
    byte[] OriginalPdfBytes,
    IReadOnlyList<SealedFieldRender> SignedFields,
    IReadOnlyList<PartialCopySignerStatus> SignerProgress,
    string RecipientSignerDisplayName,
    string DocumentTitle,
    bool SendSealedToSigners
);

public sealed record PartialCopyResult(byte[] PdfBytes, string ChecksumSha256);

public sealed record PartialCopySignerStatus(string DisplayName, PartialCopySignerState State, DateTime? SignedAtUtc);

public enum PartialCopySignerState
{
    Pending = 0,
    Signed = 1,
    Rejected = 2,
}
