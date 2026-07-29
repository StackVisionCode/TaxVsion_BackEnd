using TaxVision.Auth.Domain.Onboarding.EmailVerification;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 5 — ciclo de vida del agregado EmailVerificationChallenge (OTP).</summary>
public sealed class EmailVerificationChallengeTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private static EmailVerificationChallenge Valid(string otpCode = "123456") =>
        EmailVerificationChallenge.Create("owner@castillotax.com", otpCode, Now, Ttl).Value;

    [Fact]
    public void Create_normalizes_email_and_succeeds()
    {
        var result = EmailVerificationChallenge.Create("  Owner@CastilloTax.com ", "123456", Now, Ttl);

        Assert.True(result.IsSuccess);
        Assert.Equal("owner@castillotax.com", result.Value.Email);
        Assert.Equal(Now.Add(Ttl), result.Value.ExpiresAtUtc);
        Assert.Equal(0, result.Value.Attempts);
        Assert.Equal(0, result.Value.ResendCount);
        Assert.Null(result.Value.VerifiedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData(null)]
    public void Create_fails_for_invalid_email(string? email)
    {
        var result = EmailVerificationChallenge.Create(email!, "123456", Now, Ttl);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.Email", result.Error.Code);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_fails_for_invalid_otp_code(string? code)
    {
        var result = EmailVerificationChallenge.Create("owner@castillotax.com", code!, Now, Ttl);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.OtpCode", result.Error.Code);
    }

    [Fact]
    public void Create_fails_for_non_positive_ttl()
    {
        var result = EmailVerificationChallenge.Create("owner@castillotax.com", "123456", Now, TimeSpan.Zero);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.OtpTtl", result.Error.Code);
    }

    [Fact]
    public void Verify_succeeds_with_correct_code()
    {
        var challenge = Valid();

        var result = challenge.Verify("123456", Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(1), challenge.VerifiedAtUtc);
        Assert.Equal(0, challenge.Attempts);
    }

    [Fact]
    public void Verify_is_idempotent_when_already_verified()
    {
        var challenge = Valid();
        challenge.Verify("123456", Now.AddMinutes(1));

        var result = challenge.Verify("000000", Now.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(1), challenge.VerifiedAtUtc);
    }

    [Fact]
    public void Verify_fails_when_expired()
    {
        var challenge = Valid();

        var result = challenge.Verify("123456", Now.Add(Ttl).AddSeconds(1));

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.OtpExpired", result.Error.Code);
    }

    [Fact]
    public void Verify_increments_attempts_on_wrong_code()
    {
        var challenge = Valid();

        var result = challenge.Verify("000000", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.OtpMismatch", result.Error.Code);
        Assert.Equal(1, challenge.Attempts);
        Assert.Null(challenge.VerifiedAtUtc);
    }

    [Fact]
    public void Verify_locks_out_after_max_attempts()
    {
        var challenge = Valid();
        for (var i = 0; i < EmailVerificationChallenge.MaxAttempts; i++)
            challenge.Verify("000000", Now.AddMinutes(1));

        var result = challenge.Verify("123456", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.OtpLocked", result.Error.Code);
    }

    [Fact]
    public void Resend_regenerates_hash_resets_attempts_and_extends_expiry()
    {
        var challenge = Valid();
        challenge.Verify("000000", Now.AddMinutes(1));
        Assert.Equal(1, challenge.Attempts);

        var result = challenge.Resend("654321", Now.AddMinutes(2), Ttl);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, challenge.Attempts);
        Assert.Equal(1, challenge.ResendCount);
        Assert.Equal(Now.AddMinutes(2).Add(Ttl), challenge.ExpiresAtUtc);
        Assert.True(challenge.Verify("654321", Now.AddMinutes(3)).IsSuccess);
    }

    [Fact]
    public void Resend_fails_when_already_verified()
    {
        var challenge = Valid();
        challenge.Verify("123456", Now.AddMinutes(1));

        var result = challenge.Resend("654321", Now.AddMinutes(2), Ttl);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.AlreadyVerified", result.Error.Code);
    }

    [Fact]
    public void Resend_fails_after_max_resends()
    {
        var challenge = Valid();
        for (var i = 0; i < EmailVerificationChallenge.MaxResends; i++)
            challenge.Resend("654321", Now.AddMinutes(1), Ttl);

        var result = challenge.Resend("111111", Now.AddMinutes(1), Ttl);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.ResendLimitExceeded", result.Error.Code);
    }

    [Fact]
    public void Resend_fails_for_invalid_code_or_ttl()
    {
        var challenge = Valid();

        Assert.Equal("Onboarding.OtpCode", challenge.Resend("bad", Now.AddMinutes(1), Ttl).Error.Code);
        Assert.Equal("Onboarding.OtpTtl", challenge.Resend("654321", Now.AddMinutes(1), TimeSpan.Zero).Error.Code);
    }
}
