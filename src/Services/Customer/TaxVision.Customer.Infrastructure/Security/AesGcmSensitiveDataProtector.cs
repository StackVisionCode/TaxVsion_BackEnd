using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Infrastructure.Security;

/// <summary>
/// Cifrado AES-256-GCM con clave maestra rotativa (actual + anterior, sección "Encryption").
/// Blind index con HMAC-SHA256 cuya clave se deriva por tenant vía HKDF.
///
/// <para>B-9: el ciphertext es auto-describible. Los blobs nuevos llevan cabecera
/// <c>[magic][versión]</c> y se descifran con la clave de esa versión (actual o anterior). Los blobs
/// viejos (sin cabecera, el formato de hoy) se siguen descifrando con la clave actual, así que no hace
/// falta migrar la columna ni re-cifrar para leerlos. La rotación real re-cifra con el job (X-9).</para>
/// </summary>
public sealed class AesGcmSensitiveDataProtector : ISensitiveDataProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits
    private const byte FormatMagic = 0x01; // marca de blob versionado
    private const int VersionHeaderSize = 1 + sizeof(short); // magic + versión

    private readonly byte[] _currentKey;
    private readonly short _currentKeyVersion;
    private readonly IReadOnlyDictionary<short, byte[]> _keysByVersion;

    // El blind index se deriva de la clave actual, igual que hoy: su salida no cambia mientras no rote
    // la clave. Una rotación obliga a recomputarlo junto con el re-cifrado (ver bitácora X-9/B-9).
    private readonly byte[] _blindIndexKey;

    // Claves de blind index en orden de preferencia (actual primero, anterior después) para el lookup dual
    // durante la ventana de rotación: un BI almacenado con la clave vieja aún casa hasta que el sweep lo mueva.
    private readonly IReadOnlyList<byte[]> _blindIndexKeys;

    public short CurrentKeyVersion => _currentKeyVersion;

    public AesGcmSensitiveDataProtector(IOptions<RotatingSecretProtectionOptions> options)
    {
        var config = options.Value;
        _currentKey = DecodeKey(config.MasterKey, "Encryption:MasterKey");
        _currentKeyVersion = config.MasterKeyVersion;
        _blindIndexKey = _currentKey;

        var keys = new Dictionary<short, byte[]> { [config.MasterKeyVersion] = _currentKey };
        var blindIndexKeys = new List<byte[]> { _currentKey };
        if (!string.IsNullOrWhiteSpace(config.PreviousMasterKey))
        {
            if (config.PreviousMasterKeyVersion is not { } previousVersion)
                throw new InvalidOperationException(
                    "Encryption:PreviousMasterKeyVersion must be set when Encryption:PreviousMasterKey is configured."
                );
            var previousKey = DecodeKey(config.PreviousMasterKey, "Encryption:PreviousMasterKey");
            keys[previousVersion] = previousKey;
            blindIndexKeys.Add(previousKey);
        }
        _keysByVersion = keys;
        _blindIndexKeys = blindIndexKeys;
    }

    public byte[] Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("plainText is empty.", nameof(plainText));

        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_currentKey, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        // Layout versionado: magic | version | nonce | cipher | tag
        var combined = new byte[VersionHeaderSize + NonceSize + cipher.Length + TagSize];
        combined[0] = FormatMagic;
        BinaryPrimitives.WriteInt16BigEndian(combined.AsSpan(1, sizeof(short)), _currentKeyVersion);
        Buffer.BlockCopy(nonce, 0, combined, VersionHeaderSize, NonceSize);
        Buffer.BlockCopy(cipher, 0, combined, VersionHeaderSize + NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, VersionHeaderSize + NonceSize + cipher.Length, TagSize);
        return combined;
    }

    public string Unprotect(byte[] cipher)
    {
        if (cipher is null || cipher.Length < NonceSize + TagSize)
            throw new ArgumentException("cipher blob is too short.", nameof(cipher));

        // Un blob viejo (sin cabecera) empieza con un nonce aleatorio: puede colisionar con el magic por
        // azar. Si la vía versionada no valida el tag, se cae al formato legacy; solo así queda ambiguo.
        if (cipher.Length >= VersionHeaderSize + NonceSize + TagSize && cipher[0] == FormatMagic)
        {
            if (TryDecryptVersioned(cipher, out var versioned))
                return versioned;
        }

        return DecryptLegacy(cipher);
    }

    public string ComputeBlindIndex(string plainText, Guid tenantId)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("plainText is empty.", nameof(plainText));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        return ComputeBlindIndexWith(_blindIndexKey, plainText, tenantId);
    }

    // Lookup dual (B-9/X-9): el BI con la clave actual primero —idéntico a ComputeBlindIndex, retrocompat— y
    // el de la clave anterior si está configurada. Deduplicado: sin rotación es un solo candidato = hoy.
    public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("plainText is empty.", nameof(plainText));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        var candidates = new List<string>(_blindIndexKeys.Count);
        foreach (var key in _blindIndexKeys)
        {
            var candidate = ComputeBlindIndexWith(key, plainText, tenantId);
            if (!candidates.Contains(candidate))
                candidates.Add(candidate);
        }
        return candidates;
    }

    private static string ComputeBlindIndexWith(byte[] blindIndexKey, string plainText, Guid tenantId)
    {
        // Derivar clave por tenant via HKDF: misma master key, distinto contexto por tenant.
        var info = Encoding.UTF8.GetBytes($"blind-index:tenant:{tenantId:N}");
        var tenantKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: blindIndexKey,
            outputLength: 32,
            salt: null,
            info: info
        );

        using var hmac = new HMACSHA256(tenantKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainText));
        return Convert.ToHexString(hash); // 64 chars
    }

    private bool TryDecryptVersioned(byte[] blob, out string plainText)
    {
        plainText = string.Empty;
        var version = BinaryPrimitives.ReadInt16BigEndian(blob.AsSpan(1, sizeof(short)));
        if (!_keysByVersion.TryGetValue(version, out var key))
            return false; // clave retirada o blob legacy que coincidió con el magic

        try
        {
            plainText = Decrypt(key, blob.AsSpan(VersionHeaderSize));
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    // Formato de hoy: nonce | cipher | tag, con la clave actual.
    private string DecryptLegacy(byte[] blob) => Decrypt(_currentKey, blob);

    private static string Decrypt(byte[] key, ReadOnlySpan<byte> body)
    {
        var ciphertextLength = body.Length - NonceSize - TagSize;
        var nonce = body[..NonceSize];
        var ciphertext = body.Slice(NonceSize, ciphertextLength);
        var tag = body.Slice(NonceSize + ciphertextLength, TagSize);

        var plain = new byte[ciphertextLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DecodeKey(string configuredValue, string configPath)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
            throw new InvalidOperationException($"{configPath} must be configured (base64, 32 bytes).");

        var key = Convert.FromBase64String(configuredValue);
        if (key.Length != KeySize)
            throw new InvalidOperationException($"{configPath} must be exactly {KeySize} bytes (base64).");

        return key;
    }
}
