namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>Masked preparer identity referenced by the evidence certificate.</summary>
public sealed record CertificatePreparerEntry(string DisplayName, string MaskedIdentifier, DateTime? SignedAtUtc);
