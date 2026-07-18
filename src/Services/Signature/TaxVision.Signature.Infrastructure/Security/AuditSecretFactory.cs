using System.Security.Cryptography;
using BuildingBlocks.Security;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Genera 32 bytes crypto-random, los codifica en base64 y los cifra con AES-256-GCM
/// vía <see cref="ISecretProtector"/>. La salida es la cadena que se guarda en
/// <c>TenantSignatureSettings.AuditSecretEncrypted</c>.
/// </summary>
public sealed class AuditSecretFactory(ISecretProtector protector) : IAuditSecretFactory
{
    private const int SecretByteLength = 32;

    public string GenerateProtected()
    {
        var raw = RandomNumberGenerator.GetBytes(SecretByteLength);
        var plaintext = Convert.ToBase64String(raw);
        return protector.Protect(plaintext);
    }
}
