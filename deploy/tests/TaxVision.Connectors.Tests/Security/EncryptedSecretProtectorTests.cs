using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Security;

namespace TaxVision.Connectors.Tests.Security;

public class EncryptedSecretProtectorTests
{
    private static EncryptedSecretProtector CreateProtector()
    {
        var options = new RotatingSecretProtectionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            MasterKeyVersion = 1,
        };
        var rotatingProtector = new AesGcmRotatingSecretProtector(Options.Create(options));
        return new EncryptedSecretProtector(rotatingProtector);
    }

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsThroughDomainPort()
    {
        var protector = CreateProtector();

        var secret = protector.Protect("1//refresh-token-value");
        var plaintext = protector.Unprotect(secret);

        Assert.Equal("1//refresh-token-value", plaintext);
        Assert.Equal(EncryptedSecret.NonceLength, secret.Nonce.Length);
        Assert.Equal(EncryptedSecret.TagLength, secret.Tag.Length);
    }
}
