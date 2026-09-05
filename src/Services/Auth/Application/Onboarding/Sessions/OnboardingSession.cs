namespace TaxVision.Auth.Application.Onboarding.Sessions;

public sealed record OnboardingSession(
    string Purpose,
    Guid EmailVerificationChallengeId,
    string Email,
    Guid? OnboardingId,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc
)
{
    public const string OnboardingPurpose = "onboarding-session";
}
