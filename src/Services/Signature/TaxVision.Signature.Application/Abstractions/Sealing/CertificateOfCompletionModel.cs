namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>Pure evidence model rendered independently from EF entities.</summary>
public sealed record CertificateOfCompletionModel(
    Guid SignatureRequestId,
    string Title,
    string Category,
    DateTime CreatedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyList<CertificateSignerEntry> SignersGlobal,
    IReadOnlyList<CertificateDocumentEntry> Documents,
    string? IssuerName = null,
    byte[]? PlatformLogo = null,
    byte[]? TenantLogo = null,
    CertificatePreparerEntry? Preparer = null,
    // Cert que selló los documentos (null si el sellado CMS está degradado): alimenta el panel "Digital seal".
    CertificateSealInfo? Seal = null
);
