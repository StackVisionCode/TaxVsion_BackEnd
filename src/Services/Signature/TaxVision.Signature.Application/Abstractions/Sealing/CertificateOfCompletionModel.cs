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
    SignatureCategory Category,
    DateTime CreatedAtUtc,
    DateTime CompletedAtUtc,
    string DocumentHashPre,
    string DocumentHashPost,
    IReadOnlyList<CertificateSignerEntry> Signers,
    string? IssuerName = null,
    byte[]? PlatformLogo = null,
    byte[]? TenantLogo = null
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
