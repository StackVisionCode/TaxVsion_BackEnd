using BuildingBlocks.Security;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Security;

/// <summary>
/// Adapta <see cref="IRotatingSecretProtector"/> (BuildingBlocks, trabaja con partes crudas) al
/// puerto de dominio <see cref="IEncryptedSecretProtector"/> (trabaja con el VO EncryptedSecret).
/// El cifrado real vive acá — el dominio nunca conoce AES-GCM ni las master keys.
/// </summary>
public sealed class EncryptedSecretProtector(IRotatingSecretProtector protector) : IEncryptedSecretProtector
{
    public EncryptedSecret Protect(string plaintext, short? keyVersion = null)
    {
        var protectedSecret = protector.Protect(plaintext, keyVersion);
        return EncryptedSecret
            .Create(protectedSecret.Ciphertext, protectedSecret.Nonce, protectedSecret.Tag, protectedSecret.KeyVersion)
            .Value;
    }

    public string Unprotect(EncryptedSecret secret) =>
        protector.Unprotect(
            new RotatingProtectedSecret(secret.Ciphertext, secret.Nonce, secret.Tag, secret.KeyVersion)
        );
}
