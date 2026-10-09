namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>Integrity and participant evidence for one sealed request document.</summary>
public sealed record CertificateDocumentEntry(
    Guid DocumentId,
    int Order,
    string Title,
    string HashPre,
    string HashPost,
    DateTime SealedAtUtc,
    IReadOnlyList<CertificateDocumentSignerEntry> Signers
);
