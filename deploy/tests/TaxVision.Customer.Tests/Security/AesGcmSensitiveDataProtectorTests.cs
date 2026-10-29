using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Infrastructure.Security;

namespace TaxVision.Customer.Tests.Security;

/// <summary>
/// Caracterización + rotación del protector de datos sensibles (B-9/X-9). Los tests de caracterización
/// fijan el comportamiento de HOY —round-trip, descifrado de un blob legacy sin cabecera y salida estable
/// del blind index— para demostrar que versionar el formato no rompe lo ya guardado.
/// </summary>
public sealed class AesGcmSensitiveDataProtectorTests
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

    // ---- Caracterización ----

    [Fact]
    public void Round_trip_returns_the_plaintext()
    {
        var protector = Protector(KeyV1, 1);

        var cipher = protector.Protect(Ssn);

        Assert.Equal(Ssn, protector.Unprotect(cipher));
    }

    [Fact]
    public void A_legacy_blob_without_a_header_is_still_decrypted()
    {
        // Formato de hoy: nonce(12) | cipher | tag(16), con la clave actual y sin cabecera.
        var legacy = LegacyProtect(KeyV1, Ssn);
        var protector = Protector(KeyV1, 1);

        Assert.Equal(Ssn, protector.Unprotect(legacy));
    }

    [Fact]
    public void The_blind_index_is_deterministic_and_matches_the_reference_algorithm()
    {
        var protector = Protector(KeyV1, 1);
        var tenantId = Guid.NewGuid();

        var first = protector.ComputeBlindIndex(Ssn, tenantId);
        var second = protector.ComputeBlindIndex(Ssn, tenantId);

        Assert.Equal(first, second);
        Assert.Equal(ReferenceBlindIndex(KeyV1, Ssn, tenantId), first);
    }

    [Fact]
    public void Different_tenants_get_different_blind_indexes()
    {
        var protector = Protector(KeyV1, 1);

        Assert.NotEqual(
            protector.ComputeBlindIndex(Ssn, Guid.NewGuid()),
            protector.ComputeBlindIndex(Ssn, Guid.NewGuid())
        );
    }

    // ---- Rotación: cifrar v1 -> rotar a v2 (v1 anterior) -> leer -> re-cifrar -> quitar v1 -> leer ----

    [Fact]
    public void Full_rotation_cycle_reencrypts_and_then_retires_the_old_key()
    {
        var atV1 = Protector(KeyV1, 1).Protect(Ssn);

        var rotating = Protector(KeyV2, 2, KeyV1, 1);
        Assert.Equal(Ssn, rotating.Unprotect(atV1)); // lee con la clave anterior

        var atV2 = rotating.Protect(rotating.Unprotect(atV1)); // re-cifra con la actual

        var afterRetirement = Protector(KeyV2, 2); // v1 retirada
        Assert.Equal(Ssn, afterRetirement.Unprotect(atV2));
    }

    [Fact]
    public void A_blob_from_a_retired_key_can_no_longer_be_read()
    {
        var atV1 = Protector(KeyV1, 1).Protect(Ssn);

        var afterRetirement = Protector(KeyV2, 2); // sin v1

        Assert.Throws<AuthenticationTagMismatchException>(() => afterRetirement.Unprotect(atV1));
    }

    [Fact]
    public void A_versioned_blob_carries_its_key_version_in_the_header()
    {
        var atV2 = Protector(KeyV2, 2, KeyV1, 1).Protect(Ssn);

        Assert.Equal(0x01, atV2[0]);
        Assert.Equal(2, BinaryPrimitives.ReadInt16BigEndian(atV2.AsSpan(1, sizeof(short))));
    }

    private static byte[] LegacyProtect(byte[] key, string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);

        var combined = new byte[12 + cipher.Length + 16];
        Buffer.BlockCopy(nonce, 0, combined, 0, 12);
        Buffer.BlockCopy(cipher, 0, combined, 12, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, 12 + cipher.Length, 16);
        return combined;
    }

    private static string ReferenceBlindIndex(byte[] key, string plainText, Guid tenantId)
    {
        var info = Encoding.UTF8.GetBytes($"blind-index:tenant:{tenantId:N}");
        var tenantKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, salt: null, info);
        using var hmac = new HMACSHA256(tenantKey);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(plainText)));
    }
}
