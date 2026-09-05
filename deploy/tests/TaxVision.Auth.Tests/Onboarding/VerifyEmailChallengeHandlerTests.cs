using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.EmailVerification.Commands;
using TaxVision.Auth.Application.Onboarding.Sessions;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class VerifyEmailChallengeHandlerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Handle_issues_onboarding_session_after_successful_otp()
    {
        var challenge = EmailVerificationChallenge
            .Create("owner@castillotax.com", "123456", Now, TimeSpan.FromMinutes(10))
            .Value;
        var challenges = new FakeEmailVerificationChallengeRepository { Challenge = challenge };
        var unitOfWork = new FakeUnitOfWork();
        var store = new FakeOnboardingSessionStore();
        var sessions = new OnboardingSessionService(
            new SecureTokenService(),
            store,
            Options.Create(new OnboardingOptions { OnboardingSessionTtlMinutes = 15 })
        );

        var result = await VerifyEmailChallengeHandler.Handle(
            new VerifyEmailChallengeCommand(challenge.Id, "123456"),
            challenges,
            unitOfWork,
            sessions,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.SessionToken);
        Assert.Equal("Bearer", result.Value.TokenType);
        Assert.True(result.Value.ExpiresAtUtc > Now);
        Assert.Single(store.Sessions);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.NotNull(challenge.VerifiedAtUtc);
    }

    [Fact]
    public async Task Handle_does_not_issue_session_when_otp_fails()
    {
        var challenge = EmailVerificationChallenge
            .Create("owner@castillotax.com", "123456", Now, TimeSpan.FromMinutes(10))
            .Value;
        var challenges = new FakeEmailVerificationChallengeRepository { Challenge = challenge };
        var unitOfWork = new FakeUnitOfWork();
        var store = new FakeOnboardingSessionStore();
        var sessions = new OnboardingSessionService(
            new SecureTokenService(),
            store,
            Options.Create(new OnboardingOptions())
        );

        var result = await VerifyEmailChallengeHandler.Handle(
            new VerifyEmailChallengeCommand(challenge.Id, "000000"),
            challenges,
            unitOfWork,
            sessions,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.OtpMismatch", result.Error.Code);
        Assert.Empty(store.Sessions);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, challenge.Attempts);
    }
}
