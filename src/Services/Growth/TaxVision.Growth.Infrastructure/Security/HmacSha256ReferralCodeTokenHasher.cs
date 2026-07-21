using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure.Security;

/// <summary>
/// Keyed hash for human-entered referral codes. Its pepper is isolated from the
/// promotional Codes pepper so compromise or rotation does not cross token domains.
/// </summary>
public sealed class HmacSha256ReferralCodeTokenHasher : IReferralCodeTokenHasher
{
    private readonly byte[] _pepper;

    public HmacSha256ReferralCodeTokenHasher(IOptions<ReferralCodeTokenHashingOptions> options)
    {
        _pepper = Encoding.UTF8.GetBytes(options.Value.Pepper);
        if (_pepper.Length < 32)
        {
            throw new InvalidOperationException("The Growth code token hashing pepper must contain at least 32 bytes.");
        }
    }

    public Result<string> Hash(string referralCode)
    {
        if (string.IsNullOrWhiteSpace(referralCode))
        {
            return Result.Failure<string>(new Error("ReferralCode.Token.Required", "A referral code is required."));
        }

        var normalized = referralCode.Trim().ToUpperInvariant();
        var digest = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(normalized));
        return Result.Success(Convert.ToHexStringLower(digest));
    }
}
