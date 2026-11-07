using TaxVision.Auth.Onboarding.Application.Sessions;

namespace TaxVision.Auth.Onboarding.Application.Abstractions;

public interface IOnboardingSessionStore
{
    Task SetAsync(string sessionTokenHash, OnboardingSession session, TimeSpan ttl, CancellationToken ct = default);

    Task<OnboardingSession?> GetAsync(string sessionTokenHash, CancellationToken ct = default);

    Task RemoveAsync(string sessionTokenHash, CancellationToken ct = default);
}
