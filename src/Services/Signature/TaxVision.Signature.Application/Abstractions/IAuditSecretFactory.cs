namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Genera y cifra el secreto HMAC por tenant que alimenta la cadena de auditoría
/// tamper-evident. La implementación vive en Infrastructure porque depende de
/// <c>ISecretProtector</c> y <c>RandomNumberGenerator</c>; el dominio sólo recibe la
/// cadena cifrada.
/// </summary>
public interface IAuditSecretFactory
{
    /// <summary>
    /// Produce un secreto crypto-random de 32 bytes y lo cifra con AES-256-GCM
    /// (<c>Encryption:MasterKey</c>). Retorna la cadena base64 protegida lista para
    /// guardar en <c>TenantSignatureSettings.AuditSecretEncrypted</c>.
    /// </summary>
    string GenerateProtected();
}
