using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Infrastructure.Security;

namespace TaxVision.Customer.Tests.Application;

// La primitiva de re-cifrado de Customer: lee con la versión del blob y reescribe con la actual, y recomputa
// el blind index con el mismo texto. El sweep completo sobre los perfiles fiscales queda pendiente (ver bitácora).
public sealed class SensitiveDataReEncryptorTests
{
    private const string Ssn = "123456789";
    private static readonly byte[] KeyV1 = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] KeyV2 = RandomNumberGenerator.GetBytes(32);

    private static AesGcmSensitiveDataProtector Protector(
        byte[] currentKey,
        short currentVersion,
        byte[]? previousKey = null,
        short? previousVersion = null
    ) =>
        new(
            Options.Create(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = Convert.ToBase64String(currentKey),
                    MasterKeyVersion = currentVersion,
                    PreviousMasterKey = previousKey is null ? null : Convert.ToBase64String(previousKey),
                    PreviousMasterKeyVersion = previousVersion,
                }
            )
        );

    [Fact]
    public void Reencrypts_an_old_blob_so_the_retired_key_is_no_longer_needed()
    {
        var tenantId = Guid.NewGuid();
        var atV1 = Protector(KeyV1, 1).Protect(Ssn);

        var rotating = Protector(KeyV2, 2, KeyV1, 1);
        var upgraded = new SensitiveDataReEncryptor(rotating).ReEncrypt(atV1, tenantId);

        var afterRetirement = Protector(KeyV2, 2);
        Assert.Equal(Ssn, afterRetirement.Unprotect(upgraded.Cipher));
        Assert.Equal(afterRetirement.ComputeBlindIndex(Ssn, tenantId), upgraded.BlindIndex);
    }
}
