using BuildingBlocks.Caching;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Sessions;

/// <summary>
/// Lee la misma clave Redis que <c>Auth.Infrastructure.Security.AccessTokenDenylist</c> escribe —
/// este servicio comparte el store, no el escritor. Fail-open: si Redis no responde, se registra
/// un warning y se trata la sesión como no-denegada — un Redis caído nunca debe bloquear el tráfico
/// normal (RBAC Fase 6, riesgo documentado en el plan).
/// </summary>
public sealed class SessionDenylistReader(ICacheService cache, ILogger<SessionDenylistReader> logger)
    : ISessionDenylistReader
{
    private static string Key(Guid sessionId) => $"auth:denylist:sid:{sessionId:N}";

    public async Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            return await cache.GetAsync<bool?>(Key(sessionId), ct) == true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Session denylist check failed (Redis unavailable?) for session {SessionId} — failing open.",
                sessionId
            );
            return false;
        }
    }
}
