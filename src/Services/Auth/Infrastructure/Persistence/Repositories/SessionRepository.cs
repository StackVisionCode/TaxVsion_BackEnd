using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del repositorio de sesiones y refresh tokens:
/// alta, consulta y revocación de sesiones por sesión, usuario o tenant.
/// </summary>
public sealed class SessionRepository(AuthDbContext db) : ISessionRepository
{
    public async Task AddSessionAsync(UserSession session, CancellationToken ct = default) =>
        await db.UserSessions.AddAsync(session, ct);

    // IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (ver su comentario) — los
    // llamadores (Logout/RevokeSession/RefreshAccessToken) ya validan session.UserId/TenantId
    // post-fetch, o el sessionId viene de un refresh token ya autenticado (self).
    public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
        db.UserSessions.IgnoreQueryFilters().FirstOrDefaultAsync(session => session.Id == sessionId, ct);

    // IgnoreQueryFilters(): mismo bug — los 2 llamadores pasan el propio userId del actor
    // autenticado o un targetUserId ya validado contra el tenant (GetUserSessionsHandler).
    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .OrderByDescending(session => session.LastSeenAtUtc)
            .ToListAsync(ct);

    public async Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) =>
        await db.RefreshTokens.AddAsync(token, ct);

    // IgnoreQueryFilters(): igual razón que UserRepository.GetByEmailAsync en este mismo
    // servicio — RefreshAccessToken corre sin JWT todavía (el propio refresh token es la
    // credencial), así que el tenant nunca está poblado ahí.
    public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(token => token.TokenHash == tokenHash, ct);

    /// <summary>Revoca una sesión concreta y todos sus refresh tokens activos; devuelve cuántos tokens se revocaron.</summary>
    // IgnoreQueryFilters() en ambas queries: el sessionId que llega acá ya fue resuelto y
    // validado (tenant/ownership) por el llamador vía GetSessionByIdAsync o un refresh token
    // autenticado — sin esto, el re-fetch interno silenciosamente no encontraba nada y logout/
    // revoke no revocaba nada en la base de datos (bug real, no solo teórico).
    public async Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        var session = await db
            .UserSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(value => value.Id == sessionId, ct);
        session?.Revoke(reason);

        var tokens = await db
            .RefreshTokens.IgnoreQueryFilters()
            .Where(token => token.SessionId == sessionId && token.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(reason);

        return tokens.Count;
    }

    /// <summary>Revoca todas las sesiones y tokens del usuario, opcionalmente conservando una sesión; devuelve cuántas se revocaron.</summary>
    // IgnoreQueryFilters(): mismo bug — los 3 llamadores pasan el propio userId del actor
    // (password change, logout-all) o un target ya validado contra el tenant (Deactivate).
    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken ct = default
    )
    {
        var sessions = await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .ToListAsync(ct);

        var revoked = 0;
        foreach (var session in sessions)
        {
            if (session.Id == exceptSessionId)
                continue;
            session.Revoke(reason);
            revoked++;
        }

        var tokens = await db
            .RefreshTokens.IgnoreQueryFilters()
            .Where(token =>
                token.UserId == userId
                && token.RevokedAtUtc == null
                && (exceptSessionId == null || token.SessionId != exceptSessionId)
            )
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(reason);

        return revoked;
    }

    /// <summary>Revoca todas las sesiones y tokens de un tenant completo; devuelve cuántas sesiones se revocaron.</summary>
    public async Task<int> RevokeAllForTenantAsync(Guid tenantId, string reason, CancellationToken ct = default)
    {
        var sessions = await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session => session.TenantId == tenantId && session.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var session in sessions)
            session.Revoke(reason);

        var tokens = await db
            .RefreshTokens.IgnoreQueryFilters()
            .Where(token => token.TenantId == tenantId && token.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(reason);

        return sessions.Count;
    }
}
