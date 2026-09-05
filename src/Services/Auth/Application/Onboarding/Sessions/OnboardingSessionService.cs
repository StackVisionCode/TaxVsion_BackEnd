using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.Sessions;

public sealed class OnboardingSessionService(
    ISecureTokenService tokens,
    IOnboardingSessionStore store,
    IOptions<OnboardingOptions> options
)
{
    public async Task<Result<OnboardingSessionTicket>> IssueAsync(
        Guid emailVerificationChallengeId,
        string email,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        if (emailVerificationChallengeId == Guid.Empty)
            return Result.Failure<OnboardingSessionTicket>(
                new Error("Onboarding.ChallengeId", "Challenge id is required.")
            );

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail.Length == 0)
            return Result.Failure<OnboardingSessionTicket>(new Error("Onboarding.Email", "A valid email is required."));

        var ttl = SessionTtl();
        var rawToken = tokens.GenerateToken(byteLength: 48);
        var session = new OnboardingSession(
            OnboardingSession.OnboardingPurpose,
            emailVerificationChallengeId,
            normalizedEmail,
            OnboardingId: null,
            nowUtc,
            nowUtc.Add(ttl)
        );

        await store.SetAsync(Hash(rawToken), session, ttl, ct);
        return Result.Success(new OnboardingSessionTicket(rawToken, session.ExpiresAtUtc));
    }

    public async Task<Result<OnboardingSession>> ValidateAsync(
        string? rawToken,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return Result.Failure<OnboardingSession>(
                new Error("Onboarding.SessionRequired", "Onboarding session is required.")
            );

        var session = await store.GetAsync(Hash(rawToken), ct);
        if (session is null || session.Purpose != OnboardingSession.OnboardingPurpose)
            return Result.Failure<OnboardingSession>(
                new Error("Onboarding.SessionInvalid", "Onboarding session is invalid.")
            );

        if (nowUtc >= session.ExpiresAtUtc)
        {
            await store.RemoveAsync(Hash(rawToken), ct);
            return Result.Failure<OnboardingSession>(
                new Error("Onboarding.SessionExpired", "Onboarding session has expired.")
            );
        }

        return Result.Success(session);
    }

    public Result EnsureMatches(OnboardingSession session, string email, Guid emailVerificationChallengeId)
    {
        if (!string.Equals(session.Email, NormalizeEmail(email), StringComparison.OrdinalIgnoreCase))
            return Result.Failure(
                new Error("Onboarding.SessionEmailMismatch", "Onboarding session email does not match.")
            );

        if (session.EmailVerificationChallengeId != emailVerificationChallengeId)
        {
            return Result.Failure(
                new Error("Onboarding.SessionChallengeMismatch", "Onboarding session challenge does not match.")
            );
        }

        return Result.Success();
    }

    public Result EnsureMatches(OnboardingSession session, Guid onboardingId, string email)
    {
        if (!string.Equals(session.Email, NormalizeEmail(email), StringComparison.OrdinalIgnoreCase))
            return Result.Failure(
                new Error("Onboarding.SessionEmailMismatch", "Onboarding session email does not match.")
            );

        if (session.OnboardingId is not null && session.OnboardingId != onboardingId)
        {
            return Result.Failure(
                new Error("Onboarding.SessionOnboardingMismatch", "Onboarding session onboarding id does not match.")
            );
        }

        return Result.Success();
    }

    public Result EnsureBoundTo(OnboardingSession session, Guid onboardingId)
    {
        if (session.OnboardingId != onboardingId)
        {
            return Result.Failure(
                new Error("Onboarding.SessionOnboardingMismatch", "Onboarding session onboarding id does not match.")
            );
        }

        return Result.Success();
    }

    public async Task BindOnboardingAsync(
        string rawToken,
        OnboardingSession session,
        Guid onboardingId,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var ttl = session.ExpiresAtUtc - nowUtc;
        if (ttl <= TimeSpan.Zero)
            return;

        var updatedSession = session with { OnboardingId = onboardingId };
        await store.SetAsync(Hash(rawToken), updatedSession, ttl, ct);
    }

    private TimeSpan SessionTtl()
    {
        var minutes = options.Value.OnboardingSessionTtlMinutes;
        return TimeSpan.FromMinutes(minutes > 0 ? minutes : 30);
    }

    private string Hash(string rawToken) => tokens.Hash(rawToken.Trim()).ToLowerInvariant();

    private static string NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;
}
