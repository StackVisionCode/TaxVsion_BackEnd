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

    public Task<UserSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken ct = default) =>
        db.UserSessions.FirstOrDefaultAsync(session => session.Id == sessionId, ct);

    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsByUserAsync(
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .UserSessions.Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .OrderByDescending(session => session.LastSeenAtUtc)
            .ToListAsync(ct);

    public async Task AddTokenAsync(RefreshToken token, CancellationToken ct = default) =>
        await db.RefreshTokens.AddAsync(token, ct);

    public Task<RefreshToken?> GetTokenByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, ct);

    /// <summary>Revoca una sesión concreta y todos sus refresh tokens activos; devuelve cuántos tokens se revocaron.</summary>
    public async Task<int> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(value => value.Id == sessionId, ct);
        session?.Revoke(reason);

        var tokens = await db
            .RefreshTokens.Where(token => token.SessionId == sessionId && token.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(reason);

        return tokens.Count;
    }

    /// <summary>Revoca todas las sesiones y tokens del usuario, opcionalmente conservando una sesión; devuelve cuántas se revocaron.</summary>
    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        Guid? exceptSessionId = null,
        CancellationToken ct = default
    )
    {
        var sessions = await db
            .UserSessions.Where(session => session.UserId == userId && session.RevokedAtUtc == null)
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
            .RefreshTokens.Where(token =>
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
            .UserSessions.Where(session => session.TenantId == tenantId && session.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var session in sessions)
            session.Revoke(reason);

        var tokens = await db
            .RefreshTokens.Where(token => token.TenantId == tenantId && token.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(reason);

        return sessions.Count;
    }
}
