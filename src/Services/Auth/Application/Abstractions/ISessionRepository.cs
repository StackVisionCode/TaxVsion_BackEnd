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

    /// <summary>Revoca todas las sesiones activas del usuario (opcionalmente excepto una).</summary>
    Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken ct = default);

    /// <summary>Revoca todas las sesiones activas del tenant (suspensión).</summary>
    Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default);
}
