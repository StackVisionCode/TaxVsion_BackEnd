using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.Sessions;

/// <summary>
/// Store Redis para la referencia de retorno del checkout. Guarda <c>hash(ref) → onboardingId</c> con TTL
/// corto; el ref crudo (que viaja en la URL del successUrl) nunca se persiste, así un dump de Redis no
/// revela referencias usables. Mismo uso de <see cref="IConnectionMultiplexer"/> crudo que
/// RedisTokenReferenceStore/RedisOnboardingSessionStore.
/// </summary>
public sealed class RedisOnboardingReturnReferenceStore(IConnectionMultiplexer redis, ISecureTokenService tokens)
    : IOnboardingReturnReferenceStore
{
    public async Task<string> IssueAsync(Guid onboardingId, TimeSpan ttl, CancellationToken ct = default)
    {
        var reference = tokens.GenerateToken();
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(reference), onboardingId.ToString("N"), ttl);
        return reference;
    }

    public async Task<Guid?> ResolveAsync(string reference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(Key(reference));
        return !value.IsNullOrEmpty && Guid.TryParseExact(value.ToString(), "N", out var onboardingId)
            ? onboardingId
            : null;
    }

    private string Key(string reference) => $"auth:onboarding-return:{tokens.Hash(reference)}";
}
