using BuildingBlocks.Caching;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Denylist en Redis por sesión (claim sid). Permite invalidar access tokens
/// vigentes de inmediato al revocar una sesión, desactivar un usuario o
/// suspender un tenant. El middleware SessionDenylistMiddleware la consulta.
/// </summary>
public sealed class AccessTokenDenylist(ICacheService cache) : IAccessTokenDenylist
{
    private static string Key(Guid sessionId) => $"auth:denylist:sid:{sessionId:N}";

    public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default)
        => cache.SetAsync(Key(sessionId), true, ttl, ct);

    public async Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default)
        => await cache.GetAsync<bool?>(Key(sessionId), ct) == true;
}
