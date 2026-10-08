namespace TaxVision.Signature.Application.Abstractions.Sealing;

public sealed record CertificateResult(byte[] CertificatePdfBytes, string ChecksumSha256);
