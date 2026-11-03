using BuildingBlocks.Security;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Infrastructure.Security;

/// <summary>
/// Adapta <see cref="IRotatingSecretProtector"/> (BuildingBlocks: AES-256-GCM con clave actual + anterior,
/// sección "Encryption") al EFIN. La versión de la clave viaja con el valor, así que rotar la clave no deja
/// EFIN ilegibles mientras la anterior siga configurada.
/// </summary>
public sealed class EfinProtector(IRotatingSecretProtector protector) : IEfinProtector
{
    public EncryptedEfin Protect(Efin efin)
    {
        var secret = protector.Protect(efin.Value);
        return EncryptedEfin.Create(secret.Ciphertext, secret.Nonce, secret.Tag, secret.KeyVersion, efin.LastDigits);
    }

    public string Reveal(EncryptedEfin encrypted) =>
        protector.Unprotect(
            new RotatingProtectedSecret(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, encrypted.KeyVersion)
        );
}
