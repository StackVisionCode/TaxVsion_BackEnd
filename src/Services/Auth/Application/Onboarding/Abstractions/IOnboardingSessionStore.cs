using TaxVision.Auth.Application.Onboarding.Sessions;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public interface IOnboardingSessionStore
{
    Task SetAsync(string sessionTokenHash, OnboardingSession session, TimeSpan ttl, CancellationToken ct = default);

    Task<OnboardingSession?> GetAsync(string sessionTokenHash, CancellationToken ct = default);

    Task RemoveAsync(string sessionTokenHash, CancellationToken ct = default);
}
