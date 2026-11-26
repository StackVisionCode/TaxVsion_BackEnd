using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Sessions.Queries;

/// <summary>Una sesión para las pantallas de sesiones. Los campos después de <c>LastSeenAtUtc</c> se agregaron sin quitar ninguno.</summary>
public sealed record SessionResponse(
    Guid Id,
    string? DeviceName,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    Guid UserId = default,
    string Channel = "Web",
    string Status = "Active",
    DateTime? EndedAtUtc = null,
    string? EndReason = null,
    bool IsCurrent = false,
    string? StartIpAddress = null,
    SessionLocationResponse? Location = null,
    string? UserDisplayName = null,
    string? UserActorType = null,
    bool LocationHidden = false
)
{
    internal static SessionResponse From(
        UserSession session,
        SessionActivityWindow window,
        Guid? currentSessionId,
        bool showLocation = true
    ) =>
        new(
            session.Id,
            session.DeviceName,
            session.IpAddress,
            session.UserAgent,
            session.CreatedAtUtc,
            session.LastSeenAtUtc,
            session.UserId,
            session.Channel.ToString(),
            SessionStatuses
                .Of(
                    session.RevokedAtUtc,
                    session.RevokedReason,
                    window.Covers(session.Channel, session.CreatedAtUtc, session.LastSeenAtUtc)
                )
                .ToString(),
            session.RevokedAtUtc,
            session.RevokedReason,
            session.Id == currentSessionId,
            session.StartIpAddress,
            showLocation ? SessionLocationResponse.From(session.Location) : null,
            LocationHidden: !showLocation
        );

    internal static SessionResponse From(
        SessionHistoryRow row,
        SessionActivityWindow window,
        Guid? currentSessionId,
        bool showLocation
    ) =>
        new(
            row.Id,
            row.DeviceName,
            row.IpAddress,
            row.UserAgent,
            row.CreatedAtUtc,
            row.LastSeenAtUtc,
            row.UserId,
            row.Channel.ToString(),
            SessionStatuses
                .Of(
                    row.RevokedAtUtc,
                    row.RevokedReason,
                    window.Covers(row.Channel, row.CreatedAtUtc, row.LastSeenAtUtc)
                )
                .ToString(),
            row.RevokedAtUtc,
            row.RevokedReason,
            row.Id == currentSessionId,
            row.StartIpAddress,
            showLocation ? SessionLocationResponse.From(row.Location) : null,
            $"{row.UserName} {row.UserLastName}".Trim(),
            row.UserActorType.ToString(),
            !showLocation
        );
}

/// <summary>Ubicación aproximada (DB-IP) de una sesión.</summary>
public sealed record SessionLocationResponse(
    string CountryCode,
    string? Region,
    string? City,
    double? Latitude,
    double? Longitude,
    string Source
)
{
    internal static SessionLocationResponse? From(SessionLocation? location) =>
        location is null
            ? null
            : new(
                location.CountryCode,
                location.Region,
                location.City,
                location.Latitude,
                location.Longitude,
                location.Source
            );
}

/// <summary>Sesiones activas del usuario autenticado.</summary>
public sealed record GetMySessionsQuery(Guid UserId, Guid? CurrentSessionId = null);

public static class GetMySessionsHandler
{
    public static async Task<Result<IReadOnlyList<SessionResponse>>> Handle(
        GetMySessionsQuery query,
        ISessionRepository sessions,
        ISessionLifetimeGate gate,
        CancellationToken ct
    )
    {
        var active = await sessions.GetActiveSessionsByUserAsync(query.UserId, ct);
        var window = await gate.ActivityWindowAsync(ct);
        IReadOnlyList<SessionResponse> response = active
            .Select(session => SessionResponse.From(session, window, query.CurrentSessionId))
            .ToList();
        return Result.Success(response);
    }
}

/// <summary>Sesiones activas de un miembro del personal (requiere users.manage).</summary>
public sealed record GetUserSessionsQuery(
    Guid TenantId,
    Guid TargetUserId,
    UserActorType? RequestingActorType = null,
    Guid? CurrentSessionId = null,
    bool CanSeeLocation = false,
    Guid? RequestingUserId = null
);

public static class GetUserSessionsHandler
{
    public static async Task<Result<IReadOnlyList<SessionResponse>>> Handle(
        GetUserSessionsQuery query,
        IUserRepository users,
        ISessionRepository sessions,
        ISessionLifetimeGate gate,
        CancellationToken ct
    )
    {
        var target = await users.GetByIdAsync(query.TargetUserId, ct);
        if (target is null || target.TenantId != query.TenantId || !StaffSessions.IsStaff(target.ActorType))
        {
            return Result.Failure<IReadOnlyList<SessionResponse>>(
                new Error("User.NotFound", "User does not exist in this tenant.")
            );
        }
        if (!StaffSessions.CanReach(query.RequestingActorType, target.ActorType))
            return Result.Failure<IReadOnlyList<SessionResponse>>(SessionErrors.Forbidden);

        var active = await sessions.GetActiveSessionsByUserAsync(query.TargetUserId, ct);
        var window = await gate.ActivityWindowAsync(ct);
        var showLocation = query.CanSeeLocation || query.TargetUserId == query.RequestingUserId;
        IReadOnlyList<SessionResponse> response = active
            .Select(session => SessionResponse.From(session, window, query.CurrentSessionId, showLocation))
            .ToList();
        return Result.Success(response);
    }
}

/// <summary>Periodo y tamaño de página de las consultas de la oficina.</summary>
public static class SessionPeriod
{
    public static readonly TimeSpan DefaultSpan = TimeSpan.FromDays(7);
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(90);
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Tope de página: un número enorme desbordaría el OFFSET y daría un 500.</summary>
    public const int MaxPage = 10_000;

    /// <summary>Por defecto la última semana; más de 90 días se recorta desde el final.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Resolve(DateTime? fromUtc, DateTime? toUtc, DateTime nowUtc)
    {
        var to = toUtc ?? nowUtc;
        var from = fromUtc ?? to - DefaultSpan;
        if (from > to)
            from = to - DefaultSpan;
        if (to - from > MaxSpan)
            from = to - MaxSpan;
        return (from, to);
    }

    public static int PageSize(int? requested) => Math.Clamp(requested ?? DefaultPageSize, 1, MaxPageSize);

    public static int Page(int? requested) => Math.Clamp(requested ?? 1, 1, MaxPage);
}

public sealed record SessionPageResponse(
    IReadOnlyList<SessionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    DateTime FromUtc,
    DateTime ToUtc
);

/// <summary>Sesiones del personal de la oficina (requiere users.manage).</summary>
public sealed record GetTenantSessionsQuery(
    Guid TenantId,
    UserActorType? RequestingActorType,
    Guid? CurrentSessionId,
    Guid? UserId = null,
    SessionStatus? Status = null,
    SessionChannel? Channel = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Search = null,
    int? Page = null,
    int? PageSize = null,
    bool CanSeeLocation = false,
    Guid? RequestingUserId = null
);

public static class GetTenantSessionsHandler
{
    public static async Task<Result<SessionPageResponse>> Handle(
        GetTenantSessionsQuery query,
        ISessionHistory history,
        ISessionLifetimeGate gate,
        CancellationToken ct
    )
    {
        var (from, to) = SessionPeriod.Resolve(query.FromUtc, query.ToUtc, DateTime.UtcNow);
        var page = SessionPeriod.Page(query.Page);
        var size = SessionPeriod.PageSize(query.PageSize);
        var window = await gate.ActivityWindowAsync(ct);
        var result = await history.ListAsync(
            new SessionHistoryFilter(
                new SessionHistoryScope(query.TenantId, query.RequestingActorType == UserActorType.TenantAdmin),
                from,
                to,
                window,
                query.UserId,
                query.Status,
                query.Channel,
                string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
                page,
                size
            ),
            ct
        );
        return Result.Success(
            new SessionPageResponse(
                result
                    .Items.Select(row =>
                        SessionResponse.From(
                            row,
                            window,
                            query.CurrentSessionId,
                            query.CanSeeLocation || row.UserId == query.RequestingUserId
                        )
                    )
                    .ToList(),
                page,
                size,
                result.TotalCount,
                from,
                to
            )
        );
    }
}

public sealed record SignInsPerDay(DateOnly Date, int Count);

public sealed record SessionSummaryResponse(
    int ActiveNow,
    int SignInsInPeriod,
    int MembersWithActiveSession,
    int StaffMembers,
    int SignedOutByAdmin,
    IReadOnlyList<SignInsPerDay> SignInsPerDay,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<SessionCountryCount>? ByCountry = null,
    int? WithoutLocation = null
);

/// <summary>Conteos de la cabecera de la pantalla de sesiones (requiere users.manage).</summary>
public sealed record GetSessionSummaryQuery(
    Guid TenantId,
    UserActorType? RequestingActorType,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    bool CanSeeLocation = false
);

public static class GetSessionSummaryHandler
{
    public static async Task<Result<SessionSummaryResponse>> Handle(
        GetSessionSummaryQuery query,
        ISessionHistory history,
        ISessionLifetimeGate gate,
        ITenantRegistry tenants,
        CancellationToken ct
    )
    {
        var (from, to) = SessionPeriod.Resolve(query.FromUtc, query.ToUtc, DateTime.UtcNow);
        var window = await gate.ActivityWindowAsync(ct);
        var counts = await history.CountAsync(
            new SessionHistoryScope(query.TenantId, query.RequestingActorType == UserActorType.TenantAdmin),
            from,
            to,
            window,
            ct
        );
        var zone = OfficeZone((await tenants.GetByIdAsync(query.TenantId, ct))?.DefaultTimeZoneId);

        // Un día por fecha del periodo en la zona de la oficina, también los que no tienen inicios.
        var perDay = counts
            .SignInsUtc.GroupBy(at => LocalDate(at, zone))
            .ToDictionary(group => group.Key, group => group.Count());
        var days = new List<SignInsPerDay>();
        for (var day = LocalDate(from, zone); day <= LocalDate(to, zone); day = day.AddDays(1))
            days.Add(new SignInsPerDay(day, perDay.GetValueOrDefault(day)));

        return Result.Success(
            new SessionSummaryResponse(
                counts.ActiveNow,
                counts.SignInsUtc.Count,
                counts.MembersWithActiveSession,
                counts.StaffMembers,
                counts.SignedOutByAdmin,
                days,
                from,
                to,
                query.CanSeeLocation ? counts.ByCountry : null,
                query.CanSeeLocation ? counts.WithoutLocation : null
            )
        );
    }

    private static TimeZoneInfo OfficeZone(string? timeZoneId) =>
        timeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? zone
            : TimeZoneInfo.Utc;

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
}
