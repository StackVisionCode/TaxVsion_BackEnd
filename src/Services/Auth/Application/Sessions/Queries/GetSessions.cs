using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Sessions.Queries;

public sealed record SessionResponse(
    Guid Id,
    string? DeviceName,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc
);

/// <summary>Sesiones activas del usuario autenticado.</summary>
public sealed record GetMySessionsQuery(Guid UserId);

public static class GetMySessionsHandler
{
    public static async Task<Result<IReadOnlyList<SessionResponse>>> Handle(
        GetMySessionsQuery query,
        ISessionRepository sessions,
        CancellationToken ct
    )
    {
        var active = await sessions.GetActiveSessionsByUserAsync(query.UserId, ct);
        IReadOnlyList<SessionResponse> response = active
            .Select(session => new SessionResponse(
                session.Id,
                session.DeviceName,
                session.IpAddress,
                session.UserAgent,
                session.CreatedAtUtc,
                session.LastSeenAtUtc
            ))
            .ToList();
        return Result.Success(response);
    }
}

/// <summary>Sesiones activas de un usuario del tenant (requiere users.manage).</summary>
public sealed record GetUserSessionsQuery(Guid TenantId, Guid TargetUserId);

public static class GetUserSessionsHandler
{
    public static async Task<Result<IReadOnlyList<SessionResponse>>> Handle(
        GetUserSessionsQuery query,
        IUserRepository users,
        ISessionRepository sessions,
        CancellationToken ct
    )
    {
        var target = await users.GetByIdAsync(query.TargetUserId, ct);
        if (target is null || target.TenantId != query.TenantId)
        {
            return Result.Failure<IReadOnlyList<SessionResponse>>(
                new Error("User.NotFound", "User does not exist in this tenant.")
            );
        }

        var active = await sessions.GetActiveSessionsByUserAsync(query.TargetUserId, ct);
        IReadOnlyList<SessionResponse> response = active
            .Select(session => new SessionResponse(
                session.Id,
                session.DeviceName,
                session.IpAddress,
                session.UserAgent,
                session.CreatedAtUtc,
                session.LastSeenAtUtc
            ))
            .ToList();
        return Result.Success(response);
    }
}
