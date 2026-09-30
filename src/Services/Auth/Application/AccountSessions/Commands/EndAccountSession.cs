using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;

namespace TaxVision.Auth.Application.AccountSessions.Commands;

public sealed record EndAccountSessionCommand(string? RefreshToken);

/// <summary>
/// "Sign out" del Account. Si la sesión sigue viva en el CRM (se entró con "Manage subscription"), solo se
/// revoca la cadena del Account y el CRM no se entera. Si la sesión era solo del Account (entrada directa),
/// se cierra entera y se denylista el <c>sid</c>.
/// </summary>
public static class EndAccountSessionHandler
{
    public static async Task<Result> Handle(
        EndAccountSessionCommand command,
        ISessionRepository sessions,
        ISecureTokenService tokens,
        IAccessTokenDenylist denylist,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Result.Success();

        var stored = await sessions.GetTokenByHashAsync(tokens.Hash(command.RefreshToken), ct);
        if (stored is not { SessionId: Guid sessionId } || stored.Surface != SessionSurface.Account)
            return Result.Success();

        await sessions.RevokeSurfaceTokensAsync(sessionId, SessionSurface.Account, "account_logout", ct);

        var workspaceAlive = await sessions.HasActiveChainAsync(sessionId, SessionSurface.Workspace, ct);
        if (!workspaceAlive)
        {
            await sessions.RevokeSessionAsync(sessionId, "account_logout", ct);
            await denylist.DenySessionAsync(sessionId, TimeSpan.FromMinutes(20), ct);
        }

        await audit.AddAsync(
            AuthAuditLog.Record(
                stored.TenantId,
                stored.UserId,
                AuthAuditAction.AccountSessionEnded,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Session",
                targetId: sessionId,
                detailsJson: workspaceAlive ? """{"sessionKept":true}""" : """{"sessionKept":false}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
