using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;

namespace TaxVision.Connectors.Tests.Security;

public class AesGcmRotatingSecretProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmRotatingSecretProtector CreateProtector(RotatingSecretProtectionOptions options) =>
        new(Options.Create(options));

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsPlaintext()
    {
        var protector = CreateProtector(
            new RotatingSecretProtectionOptions { MasterKey = NewKey(), MasterKeyVersion = 1 }
        );

        var secret = protector.Protect("ya29.a0AfH6...access-token");
        var plaintext = protector.Unprotect(secret);

        Assert.Equal("ya29.a0AfH6...access-token", plaintext);
        Assert.Equal((short)1, secret.KeyVersion);
    }

    [Fact]
    public void Protect_ProducesDifferentCiphertextEachTime()
    {
        var protector = CreateProtector(
            new RotatingSecretProtectionOptions { MasterKey = NewKey(), MasterKeyVersion = 1 }
        );

        var first = protector.Protect("same-plaintext");
        var second = protector.Protect("same-plaintext");

        Assert.False(first.Ciphertext.AsSpan().SequenceEqual(second.Ciphertext));
        Assert.False(first.Nonce.AsSpan().SequenceEqual(second.Nonce));
    }

    [Fact]
    public void KeyRotation_DecryptsOldSecretViaPreviousKeyFallback()
    {
        var oldKey = NewKey();
        var newKey = NewKey();

        var beforeRotation = CreateProtector(
            new RotatingSecretProtectionOptions { MasterKey = oldKey, MasterKeyVersion = 1 }
        );
        var secretEncryptedWithOldKey = beforeRotation.Protect("refresh-token-encrypted-before-rotation");

        var afterRotation = CreateProtector(
            new RotatingSecretProtectionOptions
            {
                MasterKey = newKey,
                MasterKeyVersion = 2,
                PreviousMasterKey = oldKey,
                PreviousMasterKeyVersion = 1,
            }
        );

        var decrypted = afterRotation.Unprotect(secretEncryptedWithOldKey);

        Assert.Equal("refresh-token-encrypted-before-rotation", decrypted);
    }

    [Fact]
    public void KeyRotation_NewSecretsUseCurrentKeyVersion()
    {
        var afterRotation = CreateProtector(
            new RotatingSecretProtectionOptions
            {
                MasterKey = NewKey(),
                MasterKeyVersion = 2,
                PreviousMasterKey = NewKey(),
                PreviousMasterKeyVersion = 1,
            }
        );

        var newSecret = afterRotation.Protect("fresh-token");

        Assert.Equal((short)2, newSecret.KeyVersion);
        Assert.Equal("fresh-token", afterRotation.Unprotect(newSecret));
    }

    [Fact]
    public void Unprotect_WithUnknownKeyVersion_Throws()
    {
        var protector = CreateProtector(
            new RotatingSecretProtectionOptions { MasterKey = NewKey(), MasterKeyVersion = 2 }
        );
        var orphanSecret = new RotatingProtectedSecret(new byte[] { 1, 2, 3 }, new byte[12], new byte[16], 1);

        Assert.Throws<CryptographicException>(() => protector.Unprotect(orphanSecret));
    }

    [Fact]
    public void Constructor_WithMissingMasterKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateProtector(new RotatingSecretProtectionOptions()));
    }

    [Fact]
    public void Constructor_WithWrongSizedMasterKey_Throws()
    {
        var tooShort = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Assert.Throws<InvalidOperationException>(() =>
            CreateProtector(new RotatingSecretProtectionOptions { MasterKey = tooShort })
        );
    }

    [Fact]
    public void Constructor_WithPreviousKeyButNoPreviousVersion_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateProtector(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = NewKey(),
                    MasterKeyVersion = 2,
                    PreviousMasterKey = NewKey(),
                }
            )
        );
    }
}
