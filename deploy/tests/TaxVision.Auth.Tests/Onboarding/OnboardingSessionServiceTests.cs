using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Sessions;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class OnboardingSessionServiceTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task IssueAsync_stores_hashed_session_and_returns_raw_token_once()
    {
        var store = new FakeOnboardingSessionStore();
        var service = Service(store);
        var challengeId = Guid.NewGuid();

        var result = await service.IssueAsync(challengeId, " Owner@CastilloTax.com ", Now, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.SessionToken);
        Assert.Equal("Bearer", result.Value.TokenType);
        Assert.Equal(Now.AddMinutes(30), result.Value.ExpiresAtUtc);
        var stored = Assert.Single(store.Sessions);
        Assert.DoesNotContain(result.Value.SessionToken, stored.Key, StringComparison.Ordinal);
        Assert.Equal(challengeId, stored.Value.EmailVerificationChallengeId);
        Assert.Equal("owner@castillotax.com", stored.Value.Email);
    }

    [Fact]
    public async Task ValidateAsync_rejects_missing_invalid_and_expired_sessions()
    {
        var store = new FakeOnboardingSessionStore();
        var service = Service(store);

        var missing = await service.ValidateAsync(null, Now, CancellationToken.None);
        var invalid = await service.ValidateAsync("not-stored", Now, CancellationToken.None);
        var issued = await service.IssueAsync(Guid.NewGuid(), "owner@castillotax.com", Now, CancellationToken.None);
        var expired = await service.ValidateAsync(
            issued.Value.SessionToken,
            Now.AddMinutes(31),
            CancellationToken.None
        );

        Assert.Equal("Onboarding.SessionRequired", missing.Error.Code);
        Assert.Equal("Onboarding.SessionInvalid", invalid.Error.Code);
        Assert.Equal("Onboarding.SessionExpired", expired.Error.Code);
        Assert.Single(store.RemovedHashes);
    }

    [Fact]
    public async Task BindOnboardingAsync_updates_existing_session_without_changing_expiry()
    {
        var store = new FakeOnboardingSessionStore();
        var service = Service(store);
        var issued = await service.IssueAsync(Guid.NewGuid(), "owner@castillotax.com", Now, CancellationToken.None);
        var session = (await service.ValidateAsync(issued.Value.SessionToken, Now, CancellationToken.None)).Value;
        var onboardingId = Guid.NewGuid();

        await service.BindOnboardingAsync(issued.Value.SessionToken, session, onboardingId, Now.AddMinutes(1));

        var updated = (
            await service.ValidateAsync(issued.Value.SessionToken, Now.AddMinutes(2), CancellationToken.None)
        ).Value;
        Assert.Equal(onboardingId, updated.OnboardingId);
        Assert.Equal(issued.Value.ExpiresAtUtc, updated.ExpiresAtUtc);
    }

    [Fact]
    public async Task EnsureMatches_rejects_cross_email_or_cross_challenge_reuse()
    {
        var service = Service(new FakeOnboardingSessionStore());
        var challengeId = Guid.NewGuid();
        var issued = await service.IssueAsync(challengeId, "owner@castillotax.com", Now, CancellationToken.None);
        var session = (await service.ValidateAsync(issued.Value.SessionToken, Now, CancellationToken.None)).Value;

        var emailMismatch = service.EnsureMatches(session, "other@example.com", challengeId);
        var challengeMismatch = service.EnsureMatches(session, "owner@castillotax.com", Guid.NewGuid());

        Assert.Equal("Onboarding.SessionEmailMismatch", emailMismatch.Error.Code);
        Assert.Equal("Onboarding.SessionChallengeMismatch", challengeMismatch.Error.Code);
    }

    [Fact]
    public async Task EnsureBoundTo_requires_the_session_to_be_bound_to_that_onboarding()
    {
        var store = new FakeOnboardingSessionStore();
        var service = Service(store);
        var issued = await service.IssueAsync(Guid.NewGuid(), "owner@castillotax.com", Now, CancellationToken.None);
        var session = (await service.ValidateAsync(issued.Value.SessionToken, Now, CancellationToken.None)).Value;
        var onboardingId = Guid.NewGuid();
        await service.BindOnboardingAsync(
            issued.Value.SessionToken,
            session,
            onboardingId,
            Now,
            CancellationToken.None
        );
        var bound = (await service.ValidateAsync(issued.Value.SessionToken, Now, CancellationToken.None)).Value;

        var same = service.EnsureBoundTo(bound, onboardingId);
        var other = service.EnsureBoundTo(bound, Guid.NewGuid());

        Assert.True(same.IsSuccess);
        Assert.Equal("Onboarding.SessionOnboardingMismatch", other.Error.Code);
    }

    private static OnboardingSessionService Service(FakeOnboardingSessionStore store) =>
        new(new SecureTokenService(), store, Options.Create(new OnboardingOptions()));
}
