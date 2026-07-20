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
public sealed class HmacSha256ReferralCodeTokenGenerator
    : IReferralCodeTokenGenerator
{
    private static readonly byte[] KeyDerivationLabel = Encoding.UTF8.GetBytes(
        "TaxVision.Growth.Referrals.TokenGeneration.Key.v1"
    );
    private const string MessageDomain = "TaxVision.Growth.Referrals.TokenGeneration.Message.v1";
    private readonly byte[] _generationKey;

    public HmacSha256ReferralCodeTokenGenerator(
        IOptions<ReferralCodeTokenHashingOptions> options
    )
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

    public Result<ReferralCodeToken> Generate(
        Guid programId,
        Guid ownerId,
        string idempotencyKey
    )
    {
        if (programId == Guid.Empty || ownerId == Guid.Empty)
        {
            return Result.Failure<ReferralCodeToken>(
                new Error(
                    "ReferralCode.Token.InvalidScope",
                    "ProgramId and OwnerId are required for token generation."
                )
            );
        }

        if (
            string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Trim().Length > 200
        )
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
        var canonicalMessage =
            $"{MessageDomain}\0{programId:N}\0{ownerId:N}\0{keyLength}:{normalizedKey}";
        var digest = HMACSHA256.HashData(
            _generationKey,
            Encoding.UTF8.GetBytes(canonicalMessage)
        );

        try
        {
            // 128 unpredictable bits are sufficient for an unguessable human-facing
            // referral identifier while retaining a manageable copy/paste length.
            var clearText = $"TVR-{Convert.ToHexString(digest.AsSpan(0, 16))}";
            return ReferralCodeToken.Create(clearText);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }
}
