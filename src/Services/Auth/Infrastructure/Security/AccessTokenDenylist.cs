using BuildingBlocks.Caching;
using BuildingBlocks.Sessions;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Denylist en Redis por sesión (claim sid). Permite invalidar access tokens
/// vigentes de inmediato al revocar una sesión, desactivar un usuario o
/// suspender un tenant. El <c>BuildingBlocks.Web.Session.SessionDenylistMiddleware</c>
/// compartido (RBAC Fase 6) la consulta a través de <see cref="ISessionDenylistReader"/> — Auth
/// implementa ambas interfaces en la misma clase para no duplicar el chequeo de lectura: es el
/// único servicio que además necesita <see cref="DenySessionAsync"/> (escritura), el resto de los
/// 14 servicios solo consume <see cref="ISessionDenylistReader"/>.
/// </summary>
public sealed class AccessTokenDenylist(ICacheService cache) : IAccessTokenDenylist, ISessionDenylistReader
{
    private static string Key(Guid sessionId) => $"auth:denylist:sid:{sessionId:N}";

    public Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default) =>
        cache.SetAsync(Key(sessionId), true, ttl, ct);

    public async Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            return await cache.GetAsync<bool?>(Key(sessionId), ct) == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // H-06 — la política ante un Redis caído (seguir o cortar) la decide
            // SessionDenylistMiddleware según SessionDenylist:FailureMode, no este adaptador.
            throw new SessionDenylistUnavailableException(sessionId, ex);
        }
    }
}
