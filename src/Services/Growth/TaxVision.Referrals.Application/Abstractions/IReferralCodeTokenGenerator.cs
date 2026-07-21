using BuildingBlocks.Results;

namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>
/// Deterministically derives a referral token for one issuance operation. Implementations
/// must use a keyed cryptographic construction with explicit domain separation.
/// </summary>
public interface IReferralCodeTokenGenerator
{
    Result<ReferralCodeToken> Generate(Guid programId, Guid ownerId, string idempotencyKey);
}

/// <summary>
/// In-memory clear-text token with intentionally redacted string and JSON representations.
/// It is never a command response or a persistence model. Call <see cref="Reveal"/> only at
/// the delivery boundary or while computing the persisted keyed hash/display fragments.
/// </summary>
public sealed class ReferralCodeToken
{
    private readonly string _clearText;

    private ReferralCodeToken(string clearText) => _clearText = clearText;

    public static Result<ReferralCodeToken> Create(string clearText)
    {
        if (
            string.IsNullOrWhiteSpace(clearText)
            || clearText.Length is < 12 or > 100
            || clearText.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')
        )
        {
            return Result.Failure<ReferralCodeToken>(
                new Error(
                    "ReferralCode.Token.InvalidGeneratedValue",
                    "The generated referral token has an invalid format."
                )
            );
        }

        return Result.Success(new ReferralCodeToken(clearText));
    }

    public string Reveal() => _clearText;

    public override string ToString() => "<redacted>";
}
