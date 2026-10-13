namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>
/// Metadatos PÚBLICOS del certificado que selló los documentos del envelope, para el panel
/// "Digital seal" del Certificate of Completion. Nunca la clave privada: solo lo que cualquiera
/// vería al abrir la firma en Adobe (subject, issuer, serie, algoritmo, vigencia).
/// </summary>
public sealed record CertificateSealInfo(
    string Subject,
    string Issuer,
    string SerialNumber,
    string SignatureAlgorithm,
    DateTime NotAfterUtc
);
