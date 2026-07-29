using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Onboarding.EmailVerification;

/// <summary>
/// Desafío OTP para verificar la propiedad de un email antes de crear el TenantOnboarding
/// (PayFlow_Implementation_Plan.md §Fase 5, pasos 2-5 del PDF). Pre-tenant, como
/// TenantOnboarding: hereda BaseEntity, no AggregateRoot (ver TenantOnboarding.cs para el porqué).
/// </summary>
public sealed class EmailVerificationChallenge : BaseEntity
{
    public const int MaxAttempts = 5;
    public const int MaxResends = 5;

    private EmailVerificationChallenge() { }

    public string Email { get; private set; } = default!;
    public string OtpHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public int ResendCount { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<EmailVerificationChallenge> Create(string email, string otpCode, DateTime nowUtc, TimeSpan ttl)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
            return Result.Failure<EmailVerificationChallenge>(
                new Error("Onboarding.Email", "A valid email is required.")
            );

        if (!IsValidCode(otpCode))
            return Result.Failure<EmailVerificationChallenge>(
                new Error("Onboarding.OtpCode", "The OTP code must be 6 digits.")
            );

        if (ttl <= TimeSpan.Zero)
            return Result.Failure<EmailVerificationChallenge>(new Error("Onboarding.OtpTtl", "TTL must be positive."));

        var challenge = new EmailVerificationChallenge
        {
            Email = normalizedEmail,
            ExpiresAtUtc = nowUtc.Add(ttl),
            CreatedAtUtc = nowUtc,
            Attempts = 0,
            ResendCount = 0,
        };
        challenge.OtpHash = ComputeHash(challenge.Id, otpCode);
        return Result.Success(challenge);
    }

    public Result Verify(string rawCode, DateTime nowUtc)
    {
        if (VerifiedAtUtc is not null)
            return Result.Success(); // idempotent replay

        if (nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("Onboarding.OtpExpired", "The verification code has expired."));

        if (Attempts >= MaxAttempts)
            return Result.Failure(new Error("Onboarding.OtpLocked", "Too many failed attempts."));

        if (!IsValidCode(rawCode) || !VerifyHash(rawCode))
        {
            Attempts++;
            return Result.Failure(new Error("Onboarding.OtpMismatch", "The verification code is incorrect."));
        }

        VerifiedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Resend(string newOtpCode, DateTime nowUtc, TimeSpan ttl)
    {
        if (VerifiedAtUtc is not null)
            return Result.Failure(new Error("Onboarding.AlreadyVerified", "This challenge was already verified."));

        if (ResendCount >= MaxResends)
            return Result.Failure(new Error("Onboarding.ResendLimitExceeded", "Maximum number of resends reached."));

        if (!IsValidCode(newOtpCode))
            return Result.Failure(new Error("Onboarding.OtpCode", "The OTP code must be 6 digits."));

        if (ttl <= TimeSpan.Zero)
            return Result.Failure(new Error("Onboarding.OtpTtl", "TTL must be positive."));

        ResendCount++;
        OtpHash = ComputeHash(Id, newOtpCode);
        Attempts = 0;
        ExpiresAtUtc = nowUtc.Add(ttl);
        return Result.Success();
    }

    private bool VerifyHash(string rawCode)
    {
        var expected = Convert.FromHexString(OtpHash);
        var actual = Convert.FromHexString(ComputeHash(Id, rawCode));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string ComputeHash(Guid challengeId, string otpCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId}:{otpCode}"))).ToLowerInvariant();

    private static bool IsValidCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 6)
            return false;

        foreach (var c in code)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }
}
