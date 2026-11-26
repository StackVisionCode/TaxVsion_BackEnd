using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del repositorio de sesiones y refresh tokens:
/// alta, consulta y revocación de sesiones por sesión, usuario o tenant.
/// </summary>
public sealed class SessionRepository(AuthDbContext db) : ISessionRepository, IExpiredSessionQuery, ISessionHistory
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

    public async Task<int> RevokeSurfaceTokensAsync(
        Guid sessionId,
        SessionSurface surface,
        string reason,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters(): el sessionId sale de un refresh token o un vale ya validados.
        var tokens = await db
            .RefreshTokens.IgnoreQueryFilters()
            .Where(token => token.SessionId == sessionId && token.Surface == surface && token.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke(reason);

        return tokens.Count;
    }

    public Task<bool> HasActiveChainAsync(Guid sessionId, SessionSurface surface, CancellationToken ct = default) =>
        db
            .RefreshTokens.IgnoreQueryFilters()
            .AnyAsync(
                token =>
                    token.SessionId == sessionId
                    && token.Surface == surface
                    && token.RevokedAtUtc == null
                    && token.ExpiresAtUtc > DateTime.UtcNow,
                ct
            );

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

    /// <summary>Las sesiones vivas del tenant, para denylistearlas y anunciarlas antes de revocarlas
    /// (A5/G10). IgnoreQueryFilters() por el mismo motivo que los métodos de arriba: el llamador es un
    /// consumer de integración, sin tenant en el contexto.</summary>
    public async Task<IReadOnlyList<UserSession>> GetActiveSessionsByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session => session.TenantId == tenantId && session.RevokedAtUtc == null)
            .ToListAsync(ct);

    /// <summary>Sesiones vivas sin ningún refresh vigente (vencidos, revocados o purgados), las más viejas primero.
    /// Cross-tenant a propósito: lo usa el barrido de mantenimiento.</summary>
    public async Task<IReadOnlyList<Guid>> FindExpiredSessionIdsAsync(
        DateTime now,
        int take,
        CancellationToken ct = default
    ) =>
        await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session =>
                session.RevokedAtUtc == null
                && !db
                    .RefreshTokens.IgnoreQueryFilters()
                    .Any(token =>
                        token.SessionId == session.Id && token.RevokedAtUtc == null && token.ExpiresAtUtc > now
                    )
            )
            .OrderBy(session => session.LastSeenAtUtc)
            .Select(session => session.Id)
            .Take(take)
            .ToListAsync(ct);

    // ------------------------------------------------------------------ historial del personal (F8)
    // IgnoreQueryFilters() en todo el bloque: la oficina llega explícita en el alcance (sale del token) y
    // la purga es cross-tenant a propósito.

    public async Task<SessionHistoryPage> ListAsync(SessionHistoryFilter filter, CancellationToken ct = default)
    {
        var query = StaffSessions(filter.Scope)
            .Where(row => row.Session.CreatedAtUtc >= filter.FromUtc && row.Session.CreatedAtUtc <= filter.ToUtc);

        if (filter.UserId is { } userId)
            query = query.Where(row => row.Session.UserId == userId);
        if (filter.Channel is { } channel)
            query = query.Where(row => row.Session.Channel == channel);
        if (filter.Search is { } search)
            query = query.Where(row => (row.User.Name + " " + row.User.LastName).Contains(search));
        if (filter.Status is { } status)
            query = WithStatus(query, status, filter.Window);

        return await PageAsync(query, filter.Page, filter.PageSize, ct);
    }

    public Task<SessionHistoryPage> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query =
            from session in db.UserSessions.IgnoreQueryFilters().AsNoTracking()
            join user in db.Users.IgnoreQueryFilters().AsNoTracking() on session.UserId equals user.Id
            where
                session.TenantId == tenantId
                && session.UserId == userId
                && session.CreatedAtUtc >= fromUtc
                && session.CreatedAtUtc <= toUtc
            select new StaffSessionRow { Session = session, User = user };
        return PageAsync(query, page, pageSize, ct);
    }

    private static async Task<SessionHistoryPage> PageAsync(
        IQueryable<StaffSessionRow> query,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(row => row.Session.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new SessionHistoryRow(
                row.Session.Id,
                row.Session.UserId,
                row.User.Name,
                row.User.LastName,
                row.User.ActorType,
                row.Session.DeviceName,
                row.Session.UserAgent,
                row.Session.IpAddress,
                row.Session.StartIpAddress,
                row.Session.Channel,
                row.Session.CreatedAtUtc,
                row.Session.LastSeenAtUtc,
                row.Session.RevokedAtUtc,
                row.Session.RevokedReason,
                row.Session.Location
            ))
            .ToListAsync(ct);
        return new SessionHistoryPage(items, total);
    }

    public async Task<SessionHistoryCounts> CountAsync(
        SessionHistoryScope scope,
        DateTime fromUtc,
        DateTime toUtc,
        SessionActivityWindow window,
        CancellationToken ct = default
    )
    {
        var staff = StaffSessions(scope);
        var active = WithStatus(staff, SessionStatus.Active, window);
        var activeNow = await active.CountAsync(ct);
        var members = await active.Select(row => row.Session.UserId).Distinct().CountAsync(ct);
        var signedOutByAdmin = await staff.CountAsync(
            row =>
                row.Session.RevokedReason == StaffAdminRevoke
                && row.Session.RevokedAtUtc >= fromUtc
                && row.Session.RevokedAtUtc <= toUtc,
            ct
        );
        var signIns = await staff
            .Where(row => row.Session.CreatedAtUtc >= fromUtc && row.Session.CreatedAtUtc <= toUtc)
            .Select(row => new
            {
                row.Session.CreatedAtUtc,
                Country = row.Session.Location == null ? null : row.Session.Location.CountryCode,
            })
            .ToListAsync(ct);
        var byCountry = signIns
            .Where(signIn => signIn.Country is not null)
            .GroupBy(signIn => signIn.Country!)
            .Select(group => new SessionCountryCount(group.Key, group.Count()))
            .OrderByDescending(country => country.Count)
            .ToList();
        var staffMembers = await db
            .Users.IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                user =>
                    user.TenantId == scope.TenantId
                    && user.IsActive
                    && (
                        user.ActorType == UserActorType.TenantEmployee
                        || (scope.IncludeAdmins && user.ActorType == UserActorType.TenantAdmin)
                    ),
                ct
            );
        return new SessionHistoryCounts(
            activeNow,
            members,
            staffMembers,
            signedOutByAdmin,
            signIns.Select(signIn => signIn.CreatedAtUtc).ToList(),
            byCountry,
            signIns.Count(signIn => signIn.Country is null)
        );
    }

    public async Task<int> PurgeClosedBeforeAsync(
        DateTime closedBeforeUtc,
        int batchSize,
        CancellationToken ct = default
    )
    {
        // Sin refresh tokens colgando: la llave de los tokens hacia la sesión no borra en cascada.
        var ids = await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session =>
                session.RevokedAtUtc != null
                && session.RevokedAtUtc < closedBeforeUtc
                && !db.RefreshTokens.IgnoreQueryFilters().Any(token => token.SessionId == session.Id)
            )
            .OrderBy(session => session.RevokedAtUtc)
            .Select(session => session.Id)
            .Take(batchSize)
            .ToListAsync(ct);
        if (ids.Count == 0)
            return 0;

        return await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session => ids.Contains(session.Id))
            .ExecuteDeleteAsync(ct);
    }

    private const string StaffAdminRevoke = "admin_revoke";

    private sealed class StaffSessionRow
    {
        public required UserSession Session { get; init; }
        public required User User { get; init; }
    }

    /// <summary>Sesiones del personal de la oficina; sin las de administradores si el alcance no los incluye.</summary>
    private IQueryable<StaffSessionRow> StaffSessions(SessionHistoryScope scope) =>
        from session in db.UserSessions.IgnoreQueryFilters().AsNoTracking()
        join user in db.Users.IgnoreQueryFilters().AsNoTracking() on session.UserId equals user.Id
        where
            session.TenantId == scope.TenantId
            && (
                user.ActorType == UserActorType.TenantEmployee
                || (scope.IncludeAdmins && user.ActorType == UserActorType.TenantAdmin)
            )
        select new StaffSessionRow { Session = session, User = user };

    /// <summary>El estado de <see cref="SessionStatuses"/> traducido a SQL.</summary>
    private static IQueryable<StaffSessionRow> WithStatus(
        IQueryable<StaffSessionRow> query,
        SessionStatus status,
        SessionActivityWindow window
    )
    {
        var startedAfter = window.StartedAfterUtc;
        var seenSince = window.SeenSinceUtc;
        string[] known = SessionStatuses.KnownReasons;
        return status switch
        {
            SessionStatus.Active => query.Where(row =>
                row.Session.RevokedAtUtc == null
                && (
                    row.Session.Channel != SessionChannel.Web
                    || (row.Session.CreatedAtUtc > startedAfter && row.Session.LastSeenAtUtc >= seenSince)
                )
            ),
            SessionStatus.Expired => query.Where(row =>
                (
                    row.Session.RevokedAtUtc == null
                    && row.Session.Channel == SessionChannel.Web
                    && (row.Session.CreatedAtUtc <= startedAfter || row.Session.LastSeenAtUtc < seenSince)
                )
                || (
                    row.Session.RevokedAtUtc != null
                    && SessionStatuses.ExpiredReasons.Contains(row.Session.RevokedReason!)
                )
            ),
            SessionStatus.SignedOut => ClosedWith(query, SessionStatuses.SignedOutReasons),
            SessionStatus.Revoked => ClosedWith(query, SessionStatuses.RevokedReasons),
            SessionStatus.Replaced => ClosedWith(query, SessionStatuses.ReplacedReasons),
            _ => query.Where(row =>
                row.Session.RevokedAtUtc != null
                && (row.Session.RevokedReason == null || !known.Contains(row.Session.RevokedReason))
            ),
        };
    }

    private static IQueryable<StaffSessionRow> ClosedWith(IQueryable<StaffSessionRow> query, string[] reasons) =>
        query.Where(row => row.Session.RevokedAtUtc != null && reasons.Contains(row.Session.RevokedReason!));
}
