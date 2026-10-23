using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Security;

public sealed class FormExternalAccessSecurity : IFormExternalAccessSecurity
{
    private const int TokenBytes = 32;
    private const int OtpDigits = 6;

    private readonly IRotatingSecretProtector _protector;
    private readonly IReadOnlyDictionary<short, byte[]> _masterKeys;
    private readonly short _currentKeyVersion;

    public FormExternalAccessSecurity(
        IRotatingSecretProtector protector,
        IOptions<RotatingSecretProtectionOptions> options
    )
    {
        _protector = protector;
        var config = options.Value;
        _currentKeyVersion = config.MasterKeyVersion;

        var keys = new Dictionary<short, byte[]> { [config.MasterKeyVersion] = DecodeMasterKey(config.MasterKey) };
        if (
            !string.IsNullOrWhiteSpace(config.PreviousMasterKey)
            && config.PreviousMasterKeyVersion is { } previousVersion
        )
        {
            keys[previousVersion] = DecodeMasterKey(config.PreviousMasterKey);
        }

        _masterKeys = keys;
    }

    public FormOpaqueToken GenerateToken()
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        return new FormOpaqueToken(raw, HashToken(raw), raw[^4..]);
    }

    public FormProtectedToken GenerateProtectedToken()
    {
        var token = GenerateToken();
        var protectedToken = _protector.Protect(token.RawToken);
        return new FormProtectedToken(
            token.RawToken,
            token.TokenHash,
            token.TokenLast4,
            Pack(protectedToken),
            protectedToken.KeyVersion
        );
    }

    public string HashToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    public string UnprotectToken(string tokenCipher, short keyVersion) =>
        _protector.Unprotect(Unpack(tokenCipher, keyVersion));

    public string GenerateOtp()
    {
        var digits = new char[OtpDigits];
        for (var index = 0; index < digits.Length; index++)
            digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        return new string(digits);
    }

    public string HashOtp(Guid tenantId, Guid sourceId, string email, string code)
    {
        ValidateOtpInput(tenantId, sourceId, email, code);
        var hash = ComputeOtpHash(_masterKeys[_currentKeyVersion], tenantId, sourceId, email, code);
        return $"v{_currentKeyVersion}:{Convert.ToHexString(hash)}";
    }

    public bool VerifyOtp(Guid tenantId, Guid sourceId, string email, string code, string storedHash)
    {
        if (!TryParseStoredHash(storedHash, out var keyVersion, out var expectedHash))
            return false;
        if (!_masterKeys.TryGetValue(keyVersion, out var masterKey))
            return false;

        try
        {
            ValidateOtpInput(tenantId, sourceId, email, code);
            var actualHash = ComputeOtpHash(masterKey, tenantId, sourceId, email, code);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public FormProtectedEmail ProtectEmail(Guid tenantId, string email)
    {
        var normalized = NormalizeEmail(tenantId, email);
        var secret = _protector.Protect(normalized);
        return new FormProtectedEmail(
            Pack(secret),
            secret.KeyVersion,
            ComputeEmailLookupHash(_currentKeyVersion, tenantId, normalized)
        );
    }

    public string UnprotectEmail(string emailCipher, short keyVersion) =>
        _protector.Unprotect(Unpack(emailCipher, keyVersion));

    public IReadOnlyCollection<string> EmailLookupHashes(Guid tenantId, string email)
    {
        var normalized = NormalizeEmail(tenantId, email);
        return _masterKeys.Keys.Select(version => ComputeEmailLookupHash(version, tenantId, normalized)).ToArray();
    }

    private string ComputeEmailLookupHash(short keyVersion, Guid tenantId, string normalizedEmail)
    {
        var tenantMaterial = Encoding.UTF8.GetBytes($"client-forms-email-lookup:{tenantId:D}");
        var tenantKey = HMACSHA256.HashData(_masterKeys[keyVersion], tenantMaterial);
        var hash = HMACSHA256.HashData(tenantKey, Encoding.UTF8.GetBytes(normalizedEmail));
        return $"v{keyVersion}:{Convert.ToHexString(hash)}";
    }

    private static string NormalizeEmail(Guid tenantId, string email)
    {
        var normalized = ParticipantEmail.Create(email);
        if (tenantId == Guid.Empty || normalized.IsFailure)
            throw new ArgumentException("Tenant and a valid email are required.", nameof(email));
        return normalized.Value.Value;
    }

    private static byte[] ComputeOtpHash(byte[] masterKey, Guid tenantId, Guid sourceId, string email, string code)
    {
        var tenantMaterial = Encoding.UTF8.GetBytes($"client-forms-otp:{tenantId:D}");
        var tenantKey = HMACSHA256.HashData(masterKey, tenantMaterial);
        var normalizedEmail = ParticipantEmail.Create(email);
        if (normalizedEmail.IsFailure)
            throw new ArgumentException("The email is invalid.", nameof(email));
        var material = Encoding.UTF8.GetBytes($"{sourceId:D}:{normalizedEmail.Value.Value}:{code}");
        return HMACSHA256.HashData(tenantKey, material);
    }

    private static void ValidateOtpInput(Guid tenantId, Guid sourceId, string email, string code)
    {
        if (tenantId == Guid.Empty || sourceId == Guid.Empty)
            throw new ArgumentException("Tenant and source are required.");
        if (code is not { Length: OtpDigits } || code.Any(character => character is < '0' or > '9'))
            throw new ArgumentException("The OTP is invalid.", nameof(code));
        if (ParticipantEmail.Create(email).IsFailure)
            throw new ArgumentException("The email is invalid.", nameof(email));
    }

    private static bool TryParseStoredHash(string value, out short keyVersion, out byte[] hash)
    {
        keyVersion = default;
        hash = [];
        if (string.IsNullOrWhiteSpace(value) || value[0] != 'v')
            return false;
        var separator = value.IndexOf(':');
        if (
            separator <= 1
            || !short.TryParse(value.AsSpan(1, separator - 1), out keyVersion)
            || value.Length - separator - 1 != 64
        )
        {
            return false;
        }
        try
        {
            hash = Convert.FromHexString(value[(separator + 1)..]);
            return hash.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Pack(RotatingProtectedSecret secret) =>
        string.Join(
            '.',
            Base64UrlEncode(secret.Ciphertext),
            Base64UrlEncode(secret.Nonce),
            Base64UrlEncode(secret.Tag)
        );

    private static RotatingProtectedSecret Unpack(string value, short keyVersion)
    {
        try
        {
            var parts = value.Split('.');
            if (parts.Length != 3)
                throw new CryptographicException("The protected token is invalid.");
            return new RotatingProtectedSecret(
                Base64UrlDecode(parts[0]),
                Base64UrlDecode(parts[1]),
                Base64UrlDecode(parts[2]),
                keyVersion
            );
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The protected token is invalid.", ex);
        }
    }

    private static byte[] DecodeMasterKey(string value)
    {
        var key = Convert.FromBase64String(value);
        if (key.Length != 32)
            throw new InvalidOperationException("Encryption:MasterKey must be exactly 32 bytes (base64).");
        return key;
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
