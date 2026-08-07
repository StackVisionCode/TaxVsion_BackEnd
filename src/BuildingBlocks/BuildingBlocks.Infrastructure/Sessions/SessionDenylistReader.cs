using BuildingBlocks.Caching;
using BuildingBlocks.Sessions;

namespace BuildingBlocks.Infrastructure.Sessions;

/// <summary>
/// Lee la misma clave Redis que <c>Auth.Infrastructure.Security.AccessTokenDenylist</c> escribe —
/// este servicio comparte el store, no el escritor (RBAC Fase 6).
///
/// <para>
/// H-06 — si Redis no responde lanza <see cref="SessionDenylistUnavailableException"/> en vez de
/// devolver <c>false</c>. La política de qué hacer con esa incertidumbre es de
/// <c>SessionDenylistMiddleware</c>, que es quien tiene la configuración.
/// </para>
/// </summary>
public sealed class SessionDenylistReader(ICacheService cache) : ISessionDenylistReader
{
    private static string Key(Guid sessionId) => $"auth:denylist:sid:{sessionId:N}";

    public async Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            return await cache.GetAsync<bool?>(Key(sessionId), ct) == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SessionDenylistUnavailableException(sessionId, ex);
        }
    }
}
