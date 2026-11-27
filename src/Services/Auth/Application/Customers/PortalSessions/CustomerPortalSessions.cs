using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Customers.Commands;
using TaxVision.Auth.Application.Sessions;
using TaxVision.Auth.Application.Sessions.Commands;
using TaxVision.Auth.Application.Sessions.Queries;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Customers.PortalSessions;

// Sesiones de la cuenta del portal de un cliente, a pedido de Customer: la visibilidad del cliente la comprobó
// Customer; el actor y su permiso, el gate del M2M (SR-11). Solo alcanzan al usuario del portal de ESE cliente.

/// <summary>
/// Historial de sesiones del portal de un cliente, por fecha de inicio (máximo 90 días). Sin el permiso de
/// ubicación del actor, sin ubicación.
/// </summary>
public sealed record GetCustomerPortalSessionsQuery(
    Guid TenantId,
    Guid CustomerId,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int? Page = null,
    int? PageSize = null,
    bool ShowLocation = true
);

public static class GetCustomerPortalSessionsHandler
{
    public static async Task<Result<SessionPageResponse>> Handle(
        GetCustomerPortalSessionsQuery query,
        IUserRepository users,
        ISessionHistory history,
        ISessionLifetimeGate gate,
        CancellationToken ct
    )
    {
        var user = await users.GetPortalUserByCustomerAsync(query.TenantId, query.CustomerId, ct);
        if (user is null)
            return Result.Failure<SessionPageResponse>(ResetCustomerPortalMfaHandler.NoPortalAccount);

        var (from, to) = SessionPeriod.Resolve(query.FromUtc, query.ToUtc, DateTime.UtcNow);
        var page = SessionPeriod.Page(query.Page);
        var size = SessionPeriod.PageSize(query.PageSize);
        var window = await gate.ActivityWindowAsync(ct);
        var result = await history.ListForUserAsync(query.TenantId, user.Id, from, to, page, size, ct);
        return Result.Success(
            new SessionPageResponse(
                result.Items.Select(row => SessionResponse.From(row, window, null, query.ShowLocation)).ToList(),
                page,
                size,
                result.TotalCount,
                from,
                to
            )
        );
    }
}

/// <summary>La oficina cierra una sesión del portal del cliente.</summary>
public sealed record RevokeCustomerPortalSessionCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid SessionId,
    Guid RequestedByUserId,
    string CorrelationId,
    string? ActorType = null
);

public static class RevokeCustomerPortalSessionHandler
{
    public static async Task<Result> Handle(
        RevokeCustomerPortalSessionCommand command,
        IUserRepository users,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocationPublisher,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (user is null)
            return Result.Failure(ResetCustomerPortalMfaHandler.NoPortalAccount);

        var session = await sessions.GetSessionByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != user.Id || session.TenantId != command.TenantId)
            return Result.Failure(SessionErrors.NotFound);

        session.Revoke(StaffSessions.AdminRevokeReason, command.RequestedByUserId);
        await sessions.RevokeSessionAsync(session.Id, StaffSessions.AdminRevokeReason, ct);
        await denylist.DenySessionAsync(session.Id, SessionAccessCutoff.DenylistTtl, ct);
        // La IP y el agente serían los del servicio que llama; quién lo pidió va como actor del registro.
        await audit.AddAsync(
            AuthAuditLog.Record(
                session.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.SessionRevoked,
                true,
                ipAddress: null,
                userAgent: null,
                command.CorrelationId,
                targetType: "Session",
                targetId: session.Id,
                detailsJson: $$"""{"affectedUserId":"{{user.Id}}","customerId":"{{command.CustomerId}}"}""",
                actorType: command.ActorType
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        await revocationPublisher.PublishRevokedAsync(
            session.TenantId,
            session.UserId,
            session.Id,
            StaffSessions.AdminRevokeReason,
            ct
        );
        await bus.PublishAsync(
            SessionEndedEvents.ForSession(
                session,
                StaffSessions.AdminRevokeReason,
                DateTime.UtcNow,
                command.CorrelationId
            )
        );
        return Result.Success();
    }
}

/// <summary>La oficina cierra todas las sesiones del portal del cliente.</summary>
public sealed record RevokeCustomerPortalSessionsCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid RequestedByUserId,
    bool ForgetTrustedDevices,
    string CorrelationId,
    string? ActorType = null
);

public static class RevokeCustomerPortalSessionsHandler
{
    public static async Task<Result> Handle(
        RevokeCustomerPortalSessionsCommand command,
        IUserRepository users,
        ISessionRepository sessions,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (user is null)
            return Result.Failure(ResetCustomerPortalMfaHandler.NoPortalAccount);

        var active = await sessions.GetActiveSessionsByUserAsync(user.Id, ct);
        var revoked = await cutoff.ForUserAsync(user, StaffSessions.AdminRevokeReason, ct, command.RequestedByUserId);

        var forgotten = 0;
        if (command.ForgetTrustedDevices)
        {
            foreach (var device in await mfa.GetTrustedDevicesAsync(user.Id, ct))
            {
                if (!device.IsActive)
                    continue;
                device.Revoke();
                forgotten++;
            }
        }

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.AllSessionsRevoked,
                true,
                ipAddress: null,
                userAgent: null,
                command.CorrelationId,
                targetType: "User",
                targetId: user.Id,
                detailsJson: $$"""{"reason":"{{StaffSessions.AdminRevokeReason}}","customerId":"{{command.CustomerId}}","sessions":{{revoked}},"trustedDevicesForgotten":{{forgotten}}}""",
                actorType: command.ActorType
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var session in active)
        {
            await bus.PublishAsync(
                SessionEndedEvents.ForSession(session, StaffSessions.AdminRevokeReason, now, command.CorrelationId)
            );
        }
        return Result.Success();
    }
}
