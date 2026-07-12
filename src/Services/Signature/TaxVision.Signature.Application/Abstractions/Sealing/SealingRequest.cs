namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>
/// Datos de entrada del sellado: bytes del PDF original + campos a estampar. El engine
/// no ve la BD ni el aggregate.
/// </summary>
public sealed record SealingRequest(
    byte[] OriginalPdfBytes,
    IReadOnlyList<SealedFieldRender> Fields,
    string DocumentHashPre,
    string AuditFooter
);

/// <summary>Salida del sellado: PDF resultante + su hash SHA-256 en hex-lowercase.</summary>
public sealed record SealingResult(byte[] SealedPdfBytes, string ChecksumSha256);
