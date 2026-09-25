using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.AccountSessions.Commands;

/// <summary>El CRM pide el vale con su propia sesión (<c>sub</c> + <c>sid</c> del token).</summary>
public sealed record IssueAccountHandoffCommand(Guid UserId, Guid SessionId);

public sealed record AccountHandoffView(Guid Ticket, int ExpiresInSeconds);

/// <summary>
/// "Manage subscription" en el CRM: emite un vale de un solo uso ligado a la sesión actual para que el
/// Landing sume su cadena al mismo <c>sid</c> sin takeover. Solo TenantAdmin.
/// </summary>
public static class IssueAccountHandoffHandler
{
    public static async Task<Result<AccountHandoffView>> Handle(
        IssueAccountHandoffCommand command,
        IUserRepository users,
        ISessionRepository sessions,
        IAccountHandoffTicketStore tickets,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<AccountHandoffView>(new Error("Auth.UserInactive", "User is inactive."));

        if (AccountSurfacePolicy.Check(user, mustEnrollMfa: false) is { } denied)
            return Result.Failure<AccountHandoffView>(denied);

        var session = await sessions.GetSessionByIdAsync(command.SessionId, ct);
        if (session is null || !session.IsActive || session.UserId != user.Id)
            return Result.Failure<AccountHandoffView>(new Error("Auth.SessionRevoked", "Session is no longer active."));

        var ticket = await tickets.IssueAsync(new AccountHandoffPayload(user.TenantId, user.Id, session.Id), ct);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.AccountHandoffIssued,
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

        return Result.Success(new AccountHandoffView(ticket, (int)tickets.Lifetime.TotalSeconds));
    }
}
