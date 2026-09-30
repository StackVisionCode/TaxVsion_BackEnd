using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;

namespace TaxVision.Auth.Application.AccountSessions.Commands;

public sealed record ExchangeAccountHandoffCommand(Guid Ticket);

/// <summary>
/// Canje del vale del CRM en el Landing: suma la cadena del Account a la MISMA sesión (sin takeover) y
/// emite tokens de superficie Account. Revocar esa sesión (logout del CRM, takeover, desactivación) corta
/// también al Account.
/// </summary>
public static class ExchangeAccountHandoffHandler
{
    public static async Task<Result<AuthTokensResponse>> Handle(
        ExchangeAccountHandoffCommand command,
        IAccountHandoffTicketStore tickets,
        IUserRepository users,
        ITenantRegistry tenants,
        IRoleRepository roles,
        ISessionRepository sessions,
        IAuthSessionIssuer issuer,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Vale inválido, vencido o ya usado son indistinguibles a propósito.
        var invalid = new Error("Auth.HandoffInvalid", "The link is invalid or has expired.");

        var payload = await tickets.ConsumeAsync(command.Ticket, ct);
        if (payload is null)
            return Result.Failure<AuthTokensResponse>(invalid);

        var tenant = await tenants.GetByIdAsync(payload.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<AuthTokensResponse>(invalid);

        var user = await users.GetByIdAsync(payload.UserId, ct);
        if (user is null || user.TenantId != payload.TenantId || !user.IsActive)
            return Result.Failure<AuthTokensResponse>(invalid);

        if (AccountSurfacePolicy.Check(user, mustEnrollMfa: false) is { } denied)
            return Result.Failure<AuthTokensResponse>(denied);

        // La sesión del CRM pudo cerrarse en la ventana del vale.
        var session = await sessions.GetSessionByIdAsync(payload.SessionId, ct);
        if (session is null || !session.IsActive || session.UserId != user.Id)
            return Result.Failure<AuthTokensResponse>(invalid);

        var (roleNames, _) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        var timeZone = UserAccessResolver.EffectiveTimeZone(user, tenant);
        var issued = await issuer.JoinSessionAsync(
            session,
            user,
            timeZone,
            roleNames,
            ["handoff"],
            SessionSurface.Account,
            ct
        );
        session.Touch(request.IpAddress);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.AccountSessionStarted,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Session",
                targetId: session.Id,
                detailsJson: """{"method":"workspace_handoff"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AuthTokensResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds));
    }
}
