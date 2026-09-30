using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Persistencia de sesiones y refresh tokens. Los métodos de revocación mutan
/// entidades trackeadas; el guardado lo hace el handler vía IUnitOfWork.
/// </summary>
public interface ISessionRepository
{
    Task AddSessionAsync(UserSession session, CancellationToken ct = default);
    Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(Guid userId, CancellationToken ct = default);

    Task AddTokenAsync(RefreshToken token, CancellationToken ct = default);
    Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Revoca la sesión y todos sus refresh tokens activos. Devuelve tokens revocados.</summary>
    Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default);

    /// <summary>Revoca solo la cadena de una superficie dentro de la sesión (la sesión sigue viva). Devuelve tokens revocados.</summary>
    Task<int> RevokeSurfaceTokensAsync(
        Guid sessionId,
        SessionSurface surface,
        string reason,
        CancellationToken ct = default
    );

    /// <summary>¿La sesión tiene una cadena activa de esa superficie?</summary>
    Task<bool> HasActiveChainAsync(Guid sessionId, SessionSurface surface, CancellationToken ct = default);

    /// <summary>Revoca todas las sesiones activas del usuario (opcionalmente excepto una).</summary>
    Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken ct = default
    );

    /// <summary>Revoca todas las sesiones activas del tenant (suspensión).</summary>
    Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Las sesiones vivas del tenant. Hace falta ANTES de revocarlas: <see cref="RevokeAllForTenantAsync"/>
    /// solo devuelve un contador, y para cortar el acceso de verdad hay que denylistear cada sid y
    /// anunciarlo (A5, G10 del plan — hasta ahora la suspensión y el bloqueo por facturación revocaban
    /// solo en la base, así que el access token seguía sirviendo hasta 15 minutos).
    /// </summary>
    Task<IReadOnlyList<UserSession>> GetActiveSessionsByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserSession>>([]);
}
