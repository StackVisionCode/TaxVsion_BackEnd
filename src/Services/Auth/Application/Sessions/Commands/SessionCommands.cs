using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.Sessions.Commands;

/// <summary>Cierra la sesión actual (identificada por el claim sid del access token).</summary>
public sealed record LogoutCommand(Guid UserId, Guid SessionId);

public static class LogoutHandler
{
    public static async Task<Result> Handle(
        LogoutCommand command,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var session = await sessions.GetSessionByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.UserId)
            return Result.Success();

        await sessions.RevokeSessionAsync(session.Id, "user_logout", ct);
        await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                session.TenantId, command.UserId, AuthAuditAction.SessionRevoked, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "Session", targetId: session.Id),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>
/// Revoca una sesión concreta. El propietario siempre puede revocar las suyas;
/// un usuario con permiso users.manage puede revocar las de otros (CanManageOthers
/// lo determina el controller a partir de los claims).
/// </summary>
public sealed record RevokeSessionCommand(
    Guid RequestingUserId,
    Guid RequestingTenantId,
    Guid SessionId,
    bool CanManageOthers);

public static class RevokeSessionHandler
{
    public static async Task<Result> Handle(
        RevokeSessionCommand command,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var session = await sessions.GetSessionByIdAsync(command.SessionId, ct);
        if (session is null || session.TenantId != command.RequestingTenantId)
            return Result.Failure(new Error("Session.NotFound", "Session does not exist."));

        var isOwner = session.UserId == command.RequestingUserId;
        if (!isOwner && !command.CanManageOthers)
            return Result.Failure(new Error("Session.Forbidden", "Cannot revoke this session."));

        await sessions.RevokeSessionAsync(session.Id, isOwner ? "user_logout" : "admin_revoke", ct);
        await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                session.TenantId, command.RequestingUserId, AuthAuditAction.SessionRevoked, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "Session", targetId: session.Id),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>"Cerrar sesión en todos los dispositivos" (opcionalmente excepto el actual).</summary>
public sealed record RevokeAllMySessionsCommand(
    Guid UserId,
    Guid TenantId,
    Guid? ExceptSessionId);

public static class RevokeAllMySessionsHandler
{
    public static async Task<Result> Handle(
        RevokeAllMySessionsCommand command,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var active = await sessions.GetActiveSessionsByUserAsync(command.UserId, ct);
        foreach (var session in active)
        {
            if (session.Id == command.ExceptSessionId)
                continue;
            await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        }

        await sessions.RevokeAllForUserAsync(
            command.UserId, "user_logout_all", command.ExceptSessionId, ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId, command.UserId, AuthAuditAction.AllSessionsRevoked, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
