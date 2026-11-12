using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure.Security;

/// <summary>
/// Deterministic referral-token generation using a key derived from the referral
/// hashing pepper. The KDF label and message label keep generation cryptographically
/// separated from token hashing even though both originate from one protected secret.
/// </summary>
public sealed class HmacSha256ReferralCodeTokenGenerator : IReferralCodeTokenGenerator
{
    private static readonly byte[] KeyDerivationLabel = Encoding.UTF8.GetBytes(
        "TaxVision.Growth.Referrals.TokenGeneration.Key.v1"
    );
    private const string MessageDomain = "TaxVision.Growth.Referrals.TokenGeneration.Message.v1";
    /// <summary>Prefijo de la Idempotency-Key que pide el formato corto (lo envía el CRM al generar el código).</summary>
    public const string ShortFormatKeyPrefix = "referral-regenerate:";

    /// <summary>10 símbolos × 5 bits = 50 bits: ~1 en 10^15 de adivinar y colisión despreciable entre oficinas.</summary>
    private const int ShortCodeLength = 10;

    // Crockford base32: sin I, L, O ni U, para que se lea y dicte sin confusiones (el hasher normaliza a mayúsculas).
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly byte[] _generationKey;

    private static string EncodeCrockford(ReadOnlySpan<byte> bytes, int length)
    {
        var chars = new char[length];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        for (var i = 0; i < length; i++)
        {
            while (bits < 5)
            {
                buffer = (buffer << 8) | bytes[index++];
                bits += 8;
            }

            bits -= 5;
            chars[i] = CrockfordAlphabet[(buffer >> bits) & 0x1F];
            buffer &= (1 << bits) - 1;
        }

        return new string(chars);
    }

    public HmacSha256ReferralCodeTokenGenerator(IOptions<ReferralCodeTokenHashingOptions> options)
    {
        var rootSecret = Encoding.UTF8.GetBytes(options.Value.Pepper);
        if (rootSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "The Growth referral token root secret must contain at least 32 bytes."
            );
        }

        try
        {
            _generationKey = HMACSHA256.HashData(rootSecret, KeyDerivationLabel);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootSecret);
        }
    }

    public Result<ReferralCodeToken> Generate(Guid programId, Guid ownerId, string idempotencyKey)
    {
        if (programId == Guid.Empty || ownerId == Guid.Empty)
        {
            return Result.Failure<ReferralCodeToken>(
                new Error("ReferralCode.Token.InvalidScope", "ProgramId and OwnerId are required for token generation.")
            );
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
        {
            return Result.Failure<ReferralCodeToken>(
                new Error(
                    "ReferralCode.Token.InvalidIdempotencyKey",
                    "IdempotencyKey is required and must be 200 characters or fewer."
                )
            );
        }

        var normalizedKey = idempotencyKey.Trim();
        var keyLength = Encoding.UTF8.GetByteCount(normalizedKey);
        var canonicalMessage = $"{MessageDomain}\0{programId:N}\0{ownerId:N}\0{keyLength}:{normalizedKey}";
        var digest = HMACSHA256.HashData(_generationKey, Encoding.UTF8.GetBytes(canonicalMessage));

        try
        {
            // Códigos generados desde el CRM (clave "referral-regenerate:…") usan el formato CORTO; los emitidos antes
            // (claves "referral-code:v1:…", Postman, etc.) se derivan en el formato LARGO original para que su hash
            // guardado siga coincidiendo con el texto que se muestra. El formato lo decide solo la clave: nada
            // se persiste aparte.
            var clearText = normalizedKey.StartsWith(ShortFormatKeyPrefix, StringComparison.Ordinal)
                ? $"TVR-{EncodeCrockford(digest, ShortCodeLength)}"
                : $"TVR-{Convert.ToHexString(digest.AsSpan(0, 16))}";
            return ReferralCodeToken.Create(clearText);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }
}
