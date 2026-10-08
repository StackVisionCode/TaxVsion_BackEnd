namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>Signer evidence scoped to one completed document.</summary>
public sealed record CertificateDocumentSignerEntry(
    Guid SignerId,
    string FullName,
    DateTime SignedAtUtc,
    string? ClientIp,
    string? UserAgent
);
