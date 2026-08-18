using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 9 — UoW #1: crea el TenantOnboarding solo si el EmailVerificationChallenge
/// referenciado ya fue verificado para el mismo email.</summary>
public sealed class CreateOnboardingHandlerTests
{
    [Fact]
    public async Task Succeeds_when_the_challenge_was_verified_for_the_same_email()
    {
        var now = DateTime.UtcNow;
        var challenges = new FakeEmailVerificationChallengeRepository
        {
            Challenge = OnboardingTestFactory.VerifiedChallenge("buyer@example.com", now),
        };
        var onboardings = new FakeTenantOnboardingRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateOnboardingHandler.Handle(
            new CreateOnboardingCommand(
                "buyer@example.com",
                "Ada",
                "Lovelace",
                null,
                Guid.NewGuid(),
                challenges.Challenge!.Id
            ),
            challenges,
            onboardings,
            unitOfWork,
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(onboardings.Added);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_when_the_challenge_email_does_not_match()
    {
        var now = DateTime.UtcNow;
        var challenges = new FakeEmailVerificationChallengeRepository
        {
            Challenge = OnboardingTestFactory.VerifiedChallenge("buyer@example.com", now),
        };
        var onboardings = new FakeTenantOnboardingRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateOnboardingHandler.Handle(
            new CreateOnboardingCommand(
                "someone-else@example.com",
                "Ada",
                "Lovelace",
                null,
                Guid.NewGuid(),
                challenges.Challenge!.Id
            ),
            challenges,
            onboardings,
            unitOfWork,
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.ChallengeEmailMismatch", result.Error.Code);
        Assert.Null(onboardings.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_when_the_challenge_was_never_verified()
    {
        var now = DateTime.UtcNow;
        var unverified = EmailVerificationChallenge
            .Create("buyer@example.com", "123456", now, TimeSpan.FromMinutes(10))
            .Value;
        var challenges = new FakeEmailVerificationChallengeRepository { Challenge = unverified };
        var onboardings = new FakeTenantOnboardingRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateOnboardingHandler.Handle(
            new CreateOnboardingCommand("buyer@example.com", "Ada", "Lovelace", null, Guid.NewGuid(), unverified.Id),
            challenges,
            onboardings,
            unitOfWork,
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.EmailNotVerified", result.Error.Code);
        Assert.Null(onboardings.Added);
    }

    [Fact]
    public async Task Fails_when_the_challenge_does_not_exist()
    {
        var challenges = new FakeEmailVerificationChallengeRepository();
        var onboardings = new FakeTenantOnboardingRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateOnboardingHandler.Handle(
            new CreateOnboardingCommand("buyer@example.com", "Ada", "Lovelace", null, Guid.NewGuid(), Guid.NewGuid()),
            challenges,
            onboardings,
            unitOfWork,
            new FakeOnboardingMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.ChallengeNotFound", result.Error.Code);
    }
}
