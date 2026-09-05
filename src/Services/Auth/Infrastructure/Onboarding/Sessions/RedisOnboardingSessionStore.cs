using BuildingBlocks.Caching;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Sessions;

namespace TaxVision.Auth.Infrastructure.Onboarding.Sessions;

public sealed class RedisOnboardingSessionStore(ICacheService cache) : IOnboardingSessionStore
{
    public Task SetAsync(
        string sessionTokenHash,
        OnboardingSession session,
        TimeSpan ttl,
        CancellationToken ct = default
    ) => cache.SetAsync(Key(sessionTokenHash), session, ttl, ct);

    public Task<OnboardingSession?> GetAsync(string sessionTokenHash, CancellationToken ct = default) =>
        cache.GetAsync<OnboardingSession>(Key(sessionTokenHash), ct);

    public Task RemoveAsync(string sessionTokenHash, CancellationToken ct = default) =>
        cache.RemoveAsync(Key(sessionTokenHash), ct);

    private static string Key(string sessionTokenHash) => $"auth:onboarding-session:{sessionTokenHash}";
}
