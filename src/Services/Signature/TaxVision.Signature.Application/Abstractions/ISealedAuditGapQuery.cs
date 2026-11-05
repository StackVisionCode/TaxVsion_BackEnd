namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Documento sellado sin su línea <c>RequestSealed</c> en la cadena de auditoría.</summary>
public sealed record SealedAuditGap(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid DocumentId,
    Guid SealedFileId,
    string? DocumentHashPost,
    Guid? CertificateFileId,
    DateTime SealedAtUtc
);

/// <summary>
/// Busca documentos sellados cuya cadena no tiene una línea <c>RequestSealed</c> que los nombre, por
/// <c>DocumentId</c> o por <c>SealedFileId</c> (las líneas anteriores al multi-documento solo llevan el archivo).
/// </summary>
public interface ISealedAuditGapQuery
{
    Task<IReadOnlyList<SealedAuditGap>> ListAsync(Guid? tenantId, CancellationToken ct = default);
}
