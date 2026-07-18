using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions.Sealing;

public sealed record CertificateSignerEntry(
    string FullName,
    string Email,
    int Order,
    SignerStatus Status,
    DateTime? SignedAtUtc,
    string? ClientIp,
    string? UserAgent
);

/// <summary>
/// Modelo puro para renderizar el Certificate of Completion. No expone entities de EF ni
/// del dominio directamente — el consumer arma esto desde el aggregate para desacoplar.
/// </summary>
public sealed record CertificateOfCompletionModel(
    Guid TenantId,
    Guid SignatureRequestId,
    string Title,
    SignatureCategory Category,
    DateTime CreatedAtUtc,
    DateTime CompletedAtUtc,
    string DocumentHashPre,
    string DocumentHashPost,
    IReadOnlyList<CertificateSignerEntry> Signers
);

public sealed record CertificateResult(byte[] CertificatePdfBytes, string ChecksumSha256);

/// <summary>
/// Genera un PDF autocontenido con el detalle del cierre del proceso (firmantes, IP,
/// user agent, timestamps, hashes pre/post). Es un artefacto de audit.
/// </summary>
public interface ICertificateOfCompletionRenderer
{
    CertificateResult Render(CertificateOfCompletionModel model);
}
