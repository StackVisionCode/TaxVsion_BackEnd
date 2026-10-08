using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>Global signer timeline in the certificate.</summary>
public sealed record CertificateSignerEntry(
    Guid SignerId,
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
