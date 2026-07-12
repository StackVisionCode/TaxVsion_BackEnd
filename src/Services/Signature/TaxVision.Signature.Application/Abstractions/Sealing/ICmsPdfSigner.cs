namespace TaxVision.Signature.Application.Abstractions.Sealing;

public sealed record CmsSignedPdfResult(
    byte[] SignedPdfBytes,
    byte[] CmsSignatureBytes,
    string SignerCommonName,
    string SignatureSha256
);

/// <summary>
/// Firma un PDF con CMS/PKCS#7 (base de PAdES-B). El PDF resultante incluye la firma
/// embebida y devuelve el blob CMS separado para audit + verificación externa.
///
/// <para>
/// Es la abstracción del "sealer criptográfico". La implementación por defecto usa
/// BouncyCastle con un certificado self-signed configurado. Se puede sustituir por un
/// signer basado en HSM/Vault o por un provider comercial (DigiCert, Sectigo) sin
/// cambiar el flujo del <see cref="IDocumentSealingEngine"/>.
/// </para>
/// </summary>
public interface ICmsPdfSigner
{
    CmsSignedPdfResult Sign(byte[] visuallySealedPdfBytes);
}
