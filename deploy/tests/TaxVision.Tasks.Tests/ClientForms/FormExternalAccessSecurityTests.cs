using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Infrastructure.ClientForms.Security;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormExternalAccessSecurityTests
{
    [Fact]
    public void Tokens_are_random_hashed_and_protected_without_plaintext()
    {
        var service = Create(Key(), 2);

        var first = service.GenerateProtectedToken();
        var second = service.GenerateProtectedToken();

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.Equal(43, first.RawToken.Length);
        Assert.Equal(64, first.TokenHash.Length);
        Assert.Equal(first.RawToken[^4..], first.TokenLast4);
        Assert.DoesNotContain(first.RawToken, first.TokenCipher);
        Assert.Equal(first.RawToken, service.UnprotectToken(first.TokenCipher, first.TokenKeyVersion));
    }

    [Fact]
    public void Protected_token_rejects_tampering()
    {
        var service = Create(Key(), 3);
        var token = service.GenerateProtectedToken();
        var tampered = (token.TokenCipher[0] == 'A' ? 'B' : 'A') + token.TokenCipher[1..];

        Assert.ThrowsAny<CryptographicException>(() => service.UnprotectToken(tampered, token.TokenKeyVersion));
    }

    [Fact]
    public void Otp_is_six_digits_and_bound_to_tenant_source_and_email()
    {
        var service = Create(Key(), 4);
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var code = service.GenerateOtp();
        var hash = service.HashOtp(tenantId, sourceId, " Person@Example.com ", code);

        Assert.Matches("^[0-9]{6}$", code);
        Assert.StartsWith("v4:", hash);
        Assert.True(service.VerifyOtp(tenantId, sourceId, "person@example.com", code, hash));
        Assert.False(service.VerifyOtp(Guid.NewGuid(), sourceId, "person@example.com", code, hash));
        Assert.False(service.VerifyOtp(tenantId, Guid.NewGuid(), "person@example.com", code, hash));
        Assert.False(service.VerifyOtp(tenantId, sourceId, "other@example.com", code, hash));
        Assert.False(service.VerifyOtp(tenantId, sourceId, "person@example.com", "000000", hash));
    }

    [Fact]
    public void Otp_created_with_previous_key_verifies_during_rotation()
    {
        var oldKey = Key();
        var currentKey = Key();
        var oldService = Create(oldKey, 7);
        var rotatedService = Create(currentKey, 8, oldKey, 7);
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        const string email = "person@example.com";
        const string code = "123456";
        var oldHash = oldService.HashOtp(tenantId, sourceId, email, code);

        Assert.True(rotatedService.VerifyOtp(tenantId, sourceId, email, code, oldHash));
    }

    [Fact]
    public void Challenge_email_is_stored_encrypted_with_a_stable_tenant_bound_lookup()
    {
        var service = Create(Key(), 5);
        var tenantId = Guid.NewGuid();

        var protectedEmail = service.ProtectEmail(tenantId, " Person@Example.com ");

        Assert.DoesNotContain("person@example.com", protectedEmail.Cipher, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, protectedEmail.KeyVersion);
        Assert.Equal("person@example.com", service.UnprotectEmail(protectedEmail.Cipher, protectedEmail.KeyVersion));
        Assert.StartsWith("v5:", protectedEmail.LookupHash);
        Assert.Contains(protectedEmail.LookupHash, service.EmailLookupHashes(tenantId, "PERSON@example.com"));
        Assert.DoesNotContain(
            protectedEmail.LookupHash,
            service.EmailLookupHashes(Guid.NewGuid(), "person@example.com")
        );
    }

    [Fact]
    public void Email_lookup_written_with_the_previous_key_is_found_during_rotation()
    {
        var oldKey = Key();
        var tenantId = Guid.NewGuid();
        var stored = Create(oldKey, 7).ProtectEmail(tenantId, "person@example.com");
        var rotated = Create(Key(), 8, oldKey, 7);

        Assert.Contains(stored.LookupHash, rotated.EmailLookupHashes(tenantId, "person@example.com"));
        Assert.Equal("person@example.com", rotated.UnprotectEmail(stored.Cipher, stored.KeyVersion));
    }

    // X-9.6 — cifrar v7 -> rotar a v8 (v7 anterior) -> leer -> re-cifrar -> quitar v7 -> leer.
    [Fact]
    public void Email_reencrypted_to_the_current_key_reads_after_the_old_key_is_retired()
    {
        var oldKey = Key();
        var currentKey = Key();
        var tenantId = Guid.NewGuid();
        var stored = Create(oldKey, 7).ProtectEmail(tenantId, "person@example.com");

        var reEncrypted = Create(currentKey, 8, oldKey, 7).ReEncryptEmail(tenantId, stored.Cipher, stored.KeyVersion);
        Assert.Equal(8, reEncrypted.KeyVersion);
        Assert.StartsWith("v8:", reEncrypted.LookupHash);

        var afterRetirement = Create(currentKey, 8);
        Assert.Equal("person@example.com", afterRetirement.UnprotectEmail(reEncrypted.Cipher, reEncrypted.KeyVersion));
    }

    [Fact]
    public void Token_reencrypted_to_the_current_key_keeps_its_value_after_retirement()
    {
        var oldKey = Key();
        var currentKey = Key();
        var stored = Create(oldKey, 7).GenerateProtectedToken();

        var reEncrypted = Create(currentKey, 8, oldKey, 7).ReEncryptToken(stored.TokenCipher, stored.TokenKeyVersion);
        Assert.Equal(8, reEncrypted.KeyVersion);

        var afterRetirement = Create(currentKey, 8);
        Assert.Equal(stored.RawToken, afterRetirement.UnprotectToken(reEncrypted.Cipher, reEncrypted.KeyVersion));
    }

    private static FormExternalAccessSecurity Create(
        string currentKey,
        short currentVersion,
        string? previousKey = null,
        short? previousVersion = null
    )
    {
        var options = Options.Create(
            new RotatingSecretProtectionOptions
            {
                MasterKey = currentKey,
                MasterKeyVersion = currentVersion,
                PreviousMasterKey = previousKey,
                PreviousMasterKeyVersion = previousVersion,
            }
        );
        return new FormExternalAccessSecurity(new AesGcmRotatingSecretProtector(options), options);
    }

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
