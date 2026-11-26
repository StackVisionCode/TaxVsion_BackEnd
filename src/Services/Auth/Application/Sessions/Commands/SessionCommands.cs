using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;
using Wolverine;

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
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var session = await sessions.GetSessionByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.UserId)
            return Result.Success();

        // Logout de la sesión ACTUAL: se revoca y denylista, pero NO se publica al socket. Publicar
        // avisaría al propio dispositivo que se está deslogueando (su socket sigue vivo un instante) y
        // le dispararía el modal de "cerrada en otro dispositivo". No hay otra sesión que notificar.
        await sessions.RevokeSessionAsync(session.Id, "user_logout", ct);
        await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                session.TenantId,
                command.UserId,
                AuthAuditAction.SessionRevoked,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Session",
                targetId: session.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        // Aviso de bus (no de socket): Notification revoca el push de esta sesión.
        await bus.PublishAsync(
            SessionEndedEvents.ForSession(session, "user_logout", DateTime.UtcNow, correlation.CorrelationId)
        );
        return Result.Success();
    }
}

/// <summary>
/// Revoca una sesión concreta. El dueño puede cerrar sus otras sesiones (no la actual: para eso está el
/// logout). Con users.manage se cierran las del personal de la oficina; las de un administrador, solo
/// otro administrador. Las cuentas del portal no se alcanzan desde aquí.
/// </summary>
public sealed record RevokeSessionCommand(
    Guid RequestingUserId,
    Guid RequestingTenantId,
    Guid SessionId,
    bool CanManageOthers,
    Guid? RequestingSessionId = null,
    UserActorType? RequestingActorType = null
);

public static class RevokeSessionHandler
{
    public static async Task<Result> Handle(
        RevokeSessionCommand command,
        ISessionRepository sessions,
        IUserRepository users,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocationPublisher,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var session = await sessions.GetSessionByIdAsync(command.SessionId, ct);
        if (session is null || session.TenantId != command.RequestingTenantId)
            return Result.Failure(SessionErrors.NotFound);

        if (session.Id == command.RequestingSessionId)
            return Result.Failure(SessionErrors.CurrentSessionNotRevocable);

        var isOwner = session.UserId == command.RequestingUserId;
        if (!isOwner)
        {
            if (!command.CanManageOthers)
                return Result.Failure(SessionErrors.Forbidden);

            var owner = await users.GetByIdAsync(session.UserId, ct);
            if (owner is null || !StaffSessions.IsStaff(owner.ActorType))
                return Result.Failure(SessionErrors.NotFound);
            if (!StaffSessions.CanReach(command.RequestingActorType, owner.ActorType))
                return Result.Failure(SessionErrors.Forbidden);
        }

        var reason = isOwner ? "user_logout" : StaffSessions.AdminRevokeReason;
        session.Revoke(reason, isOwner ? null : command.RequestingUserId);
        await sessions.RevokeSessionAsync(session.Id, reason, ct);
        await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                session.TenantId,
                command.RequestingUserId,
                AuthAuditAction.SessionRevoked,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Session",
                targetId: session.Id,
                detailsJson: $$"""{"affectedUserId":"{{session.UserId}}"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        // Aviso en tiempo real al dispositivo dueño de la sesión revocada (best-effort, post-commit).
        await revocationPublisher.PublishRevokedAsync(session.TenantId, session.UserId, session.Id, reason, ct);
        await bus.PublishAsync(
            SessionEndedEvents.ForSession(session, reason, DateTime.UtcNow, correlation.CorrelationId)
        );
        return Result.Success();
    }
}

/// <summary>"Cerrar todas las sesiones" de un miembro del personal desde la gestión del equipo.</summary>
public sealed record RevokeUserSessionsCommand(
    Guid RequestingUserId,
    Guid RequestingTenantId,
    UserActorType? RequestingActorType,
    Guid TargetUserId,
    bool ForgetTrustedDevices
);

public static class RevokeUserSessionsHandler
{
    public static async Task<Result> Handle(
        RevokeUserSessionsCommand command,
        IUserRepository users,
        ISessionRepository sessions,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (command.TargetUserId == command.RequestingUserId)
            return Result.Failure(SessionErrors.SelfRevokeNotAllowed);

        var target = await users.GetByIdAsync(command.TargetUserId, ct);
        if (target is null || target.TenantId != command.RequestingTenantId || !StaffSessions.IsStaff(target.ActorType))
            return Result.Failure(new Error("User.NotFound", "User does not exist in this tenant."));
        if (!StaffSessions.CanReach(command.RequestingActorType, target.ActorType))
            return Result.Failure(SessionErrors.Forbidden);

        var active = await sessions.GetActiveSessionsByUserAsync(target.Id, ct);
        var revoked = await cutoff.ForUserAsync(target, StaffSessions.AdminRevokeReason, ct, command.RequestingUserId);

        var forgotten = 0;
        if (command.ForgetTrustedDevices)
        {
            foreach (var device in await mfa.GetTrustedDevicesAsync(target.Id, ct))
            {
                if (!device.IsActive)
                    continue;
                device.Revoke();
                forgotten++;
            }
        }

        await audit.AddAsync(
            AuthAuditLog.Record(
                target.TenantId,
                command.RequestingUserId,
                AuthAuditAction.AllSessionsRevoked,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: target.Id,
                detailsJson: $$"""{"reason":"{{StaffSessions.AdminRevokeReason}}","sessions":{{revoked}},"trustedDevicesForgotten":{{forgotten}}}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        // Un aviso por sesión, como al cerrar una: Notification retira el push de cada una.
        var now = DateTime.UtcNow;
        foreach (var session in active)
        {
            await bus.PublishAsync(
                SessionEndedEvents.ForSession(session, StaffSessions.AdminRevokeReason, now, correlation.CorrelationId)
            );
        }
        return Result.Success();
    }
}

/// <summary>Errores de la gestión de sesiones.</summary>
public static class SessionErrors
{
    public static readonly Error NotFound = new("Session.NotFound", "Session does not exist.");
    public static readonly Error Forbidden = new("Session.Forbidden", "Cannot revoke this session.");

    public static readonly Error CurrentSessionNotRevocable = new(
        "Session.CurrentSessionNotRevocable",
        "You can't sign out your current session from here."
    );

    public static readonly Error SelfRevokeNotAllowed = new(
        "Session.SelfRevokeNotAllowed",
        "You can't sign yourself out from team management."
    );
}

/// <summary>Quién es personal y a quién alcanza quien gestiona el equipo.</summary>
public static class StaffSessions
{
    public const string AdminRevokeReason = "admin_revoke";

    public static bool IsStaff(UserActorType actorType) =>
        actorType is UserActorType.TenantEmployee or UserActorType.TenantAdmin;

    /// <summary>Las sesiones de un administrador solo las ve y cierra otro administrador.</summary>
    public static bool CanReach(UserActorType? requester, UserActorType target) =>
        target != UserActorType.TenantAdmin || requester == UserActorType.TenantAdmin;
}

/// <summary>"Cerrar sesión en todos los dispositivos" (opcionalmente excepto el actual).</summary>
public sealed record RevokeAllMySessionsCommand(Guid UserId, Guid TenantId, Guid? ExceptSessionId);

public static class RevokeAllMySessionsHandler
{
    public static async Task<Result> Handle(
        RevokeAllMySessionsCommand command,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocationPublisher,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var active = await sessions.GetActiveSessionsByUserAsync(command.UserId, ct);
        foreach (var session in active)
        {
            if (session.Id == command.ExceptSessionId)
                continue;
            await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        }

        await sessions.RevokeAllForUserAsync(command.UserId, "user_logout_all", command.ExceptSessionId, ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.UserId,
                AuthAuditAction.AllSessionsRevoked,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        // Aviso en tiempo real a cada dispositivo revocado (best-effort, post-commit).
        foreach (var session in active)
        {
            if (session.Id == command.ExceptSessionId)
                continue;
            await revocationPublisher.PublishRevokedAsync(
                command.TenantId,
                command.UserId,
                session.Id,
                "user_logout_all",
                ct
            );
        }
        if (active.Any(session => session.Id != command.ExceptSessionId))
        {
            await bus.PublishAsync(
                SessionEndedEvents.ForUser(
                    command.TenantId,
                    command.UserId,
                    command.ExceptSessionId,
                    "user_logout_all",
                    DateTime.UtcNow,
                    correlation.CorrelationId
                )
            );
        }
        return Result.Success();
    }
}
