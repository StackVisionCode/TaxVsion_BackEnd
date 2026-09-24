using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>
/// Un firmante en el certificado, con su línea de tiempo (visto → consent → firmado) e IP/UA reales.
/// </summary>
public sealed record CertificateSignerEntry(
    string FullName,
    string Email,
    int Order,
    SignerStatus Status,
    DateTime? FirstViewedAtUtc,
    DateTime? ConsentAcceptedAtUtc,
    DateTime? SignedAtUtc,
    string? ClientIp,
    string? UserAgent
);

/// <summary>
/// Preparador (ERO, Form 8879) referenciado en el acta: nombre + identificador ENMASCARADO + cuándo
/// firmó. Nunca la imagen de la firma (eso vive en el documento sellado, no en el acta) — igual que la
/// industria (DocuSign Certificate of Completion referencia el evento, no incrusta la firma).
/// </summary>
public sealed record CertificatePreparerEntry(string DisplayName, string MaskedIdentifier, DateTime? SignedAtUtc);

/// <summary>
/// Modelo puro para renderizar el Certificate of Completion. No expone entities de EF ni del dominio
/// directamente — el consumer lo arma desde el aggregate para desacoplar.
///
/// <para>
/// <see cref="IssuerName"/> y los logos son opcionales: si el tenant tiene marca propia se pinta su
/// logo junto al de la plataforma; si no, queda solo la plataforma. Se resuelven en el consumer.
/// </para>
/// </summary>
public sealed record CertificateOfCompletionModel(
    Guid SignatureRequestId,
    string Title,
    string Category,
    DateTime CreatedAtUtc,
    DateTime CompletedAtUtc,
    string DocumentHashPre,
    string DocumentHashPost,
    IReadOnlyList<CertificateSignerEntry> Signers,
    string? IssuerName = null,
    byte[]? PlatformLogo = null,
    byte[]? TenantLogo = null,
    CertificatePreparerEntry? Preparer = null
);

public sealed record CertificateResult(byte[] CertificatePdfBytes, string ChecksumSha256);

/// <summary>
/// Genera un PDF autocontenido con el detalle del cierre del proceso (firmantes, IP real, user agent,
/// línea de tiempo, hashes pre/post). Es un artefacto de audit.
/// </summary>
public interface ICertificateOfCompletionRenderer
{
    CertificateResult Render(CertificateOfCompletionModel model);
}
