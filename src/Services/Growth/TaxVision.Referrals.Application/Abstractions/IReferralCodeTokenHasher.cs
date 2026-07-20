using BuildingBlocks.Results;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralCodeTokenHasher
{
    /// <summary>
    /// Hashes the complete human-entered referral token with a keyed construction.
    /// Implementations must use HMAC with a protected pepper and must never log,
    /// persist, or return the clear-text token.
    /// </summary>
    Result<string> Hash(string referralCode);
}
