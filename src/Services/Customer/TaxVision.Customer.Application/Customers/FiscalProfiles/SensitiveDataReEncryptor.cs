using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.FiscalProfiles;

/// <summary>
/// Primitiva de re-cifrado para la rotación de clave (B-9/X-9): descifra con la versión que trae el
/// blob (actual o anterior) y vuelve a cifrar con la actual. Idempotente: un blob ya en la versión
/// actual vuelve re-cifrado a la misma versión. El blind index se recomputa aparte con el mismo texto.
/// </summary>
public sealed class SensitiveDataReEncryptor(ISensitiveDataProtector protector)
{
    public ReEncryptedTaxIdentifier ReEncrypt(byte[] cipher, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        var plain = protector.Unprotect(cipher);
        return new ReEncryptedTaxIdentifier(protector.Protect(plain), protector.ComputeBlindIndex(plain, tenantId));
    }
}

public sealed record ReEncryptedTaxIdentifier(byte[] Cipher, string BlindIndex);
