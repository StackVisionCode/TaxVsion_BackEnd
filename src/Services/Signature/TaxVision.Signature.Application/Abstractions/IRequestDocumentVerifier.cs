using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>El archivo original tal como lo describe CloudStorage (proyección local o consulta directa).</summary>
public sealed record VerifiableFile(string ChecksumSha256, string ContentType, long SizeBytes);

public enum DocumentVerificationKind
{
    Verified,
    Rejected,

    /// <summary>No se pudo revisar todavía (archivo aún no disponible o descarga fallida); se reintenta luego.</summary>
    Deferred,
}

public sealed record DocumentVerificationResult(DocumentVerificationKind Kind, DocumentRejection? Rejection = null)
{
    public static readonly DocumentVerificationResult Verified = new(DocumentVerificationKind.Verified);
    public static readonly DocumentVerificationResult Deferred = new(DocumentVerificationKind.Deferred);

    public static DocumentVerificationResult Rejected(DocumentRejection rejection) =>
        new(DocumentVerificationKind.Rejected, rejection);
}

/// <summary>
/// Revisa en el servidor el PDF original de un documento (formato real, tamaño, páginas, firmas previas)
/// contra los límites del tenant y aplica el resultado al aggregate: verificado o rechazado. Es el único
/// camino para que un documento quede listo para enviar.
/// </summary>
public interface IRequestDocumentVerifier
{
    /// <summary>Usa la proyección local del archivo; si aún no está disponible, devuelve Deferred.</summary>
    Task<DocumentVerificationResult> VerifyAsync(SignatureRequest request, Guid documentId, CancellationToken ct);

    Task<DocumentVerificationResult> VerifyAsync(
        SignatureRequest request,
        Guid documentId,
        VerifiableFile file,
        CancellationToken ct
    );
}
