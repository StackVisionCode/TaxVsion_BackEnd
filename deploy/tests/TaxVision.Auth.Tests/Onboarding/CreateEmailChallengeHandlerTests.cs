using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.EmailVerification.Commands;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class CreateEmailChallengeHandlerTests
{
    private const string Email = "owner@castillotax.com";

    [Fact]
    public async Task Handle_supersedes_previous_active_challenges_for_same_email()
    {
        var challenges = new FakeEmailVerificationChallengeRepository();
        var existing = EmailVerificationChallenge
            .Create(Email, "111111", DateTime.UtcNow, TimeSpan.FromMinutes(10))
            .Value;
        challenges.Store.Add(existing);

        var result = await CreateEmailChallengeHandler.Handle(
            new CreateEmailChallengeCommand(Email, "127.0.0.1"),
            challenges,
            new PermissiveLoginThrottler(),
            new SequentialOtpGenerator("222222"),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(existing.SupersededAtUtc);

        var added = Assert.Single(challenges.Added);
        Assert.Equal(result.Value, added.Id);
        Assert.Null(added.SupersededAtUtc);
    }

    [Fact]
    public async Task Previous_challenge_becomes_invalid_after_new_one_is_created()
    {
        var challenges = new FakeEmailVerificationChallengeRepository();
        var now = DateTime.UtcNow;
        var previous = EmailVerificationChallenge.Create(Email, "111111", now, TimeSpan.FromMinutes(10)).Value;
        challenges.Store.Add(previous);

        await CreateEmailChallengeHandler.Handle(
            new CreateEmailChallengeCommand(Email, "127.0.0.1"),
            challenges,
            new PermissiveLoginThrottler(),
            new SequentialOtpGenerator("222222"),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        var fresh = Assert.Single(challenges.Added);

        var oldAttempt = previous.Verify("111111", now.AddMinutes(1));
        Assert.True(oldAttempt.IsFailure);
        Assert.Equal("Onboarding.OtpSuperseded", oldAttempt.Error.Code);

        var newAttempt = fresh.Verify("222222", now.AddMinutes(1));
        Assert.True(newAttempt.IsSuccess);
    }

    [Fact]
    public async Task Expired_or_already_verified_previous_challenges_are_left_untouched()
    {
        var challenges = new FakeEmailVerificationChallengeRepository();
        var pastNow = DateTime.UtcNow.AddHours(-1);
        var expired = EmailVerificationChallenge.Create(Email, "111111", pastNow, TimeSpan.FromMinutes(10)).Value;
        var verified = EmailVerificationChallenge
            .Create(Email, "333333", DateTime.UtcNow, TimeSpan.FromMinutes(10))
            .Value;
        verified.Verify("333333", DateTime.UtcNow.AddMinutes(1));
        challenges.Store.Add(expired);
        challenges.Store.Add(verified);

        await CreateEmailChallengeHandler.Handle(
            new CreateEmailChallengeCommand(Email, "127.0.0.1"),
            challenges,
            new PermissiveLoginThrottler(),
            new SequentialOtpGenerator("222222"),
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Null(expired.SupersededAtUtc);
        Assert.Null(verified.SupersededAtUtc);
    }

    private sealed class SequentialOtpGenerator(params string[] codes) : IOtpCodeGenerator
    {
        private int _next;

        public string Generate() => codes[_next++ % codes.Length];
    }
}
