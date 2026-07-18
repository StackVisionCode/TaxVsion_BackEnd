using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

public sealed record RefreshAccessTokenCommand(string RefreshToken);

public static class RefreshAccessTokenHandler
{
    public static async Task<Result<AuthTokensResponse>> Handle(
        RefreshAccessTokenCommand command,
        ISessionRepository sessions,
        ISecureTokenService tokens,
        IUserRepository users,
        ITenantRegistry tenants,
        IRoleRepository roles,
        IAuthSessionIssuer issuer,
        IAccessTokenDenylist denylist,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var invalid = new Error("Auth.InvalidRefreshToken", "Refresh token is invalid or expired.");

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
            return Result.Failure<AuthTokensResponse>(invalid);

        var stored = await sessions.GetTokenByHashAsync(tokens.Hash(command.RefreshToken), ct);
        if (stored is null || stored.SessionId is null)
            return Result.Failure<AuthTokensResponse>(invalid);

        // Detección de reutilización: un token ya rotado/revocado que vuelve a
        // presentarse se trata como robo — se revoca la sesión completa.
        if (!stored.IsActive)
        {
            if (stored.RevokedAtUtc is not null)
            {
                await sessions.RevokeSessionAsync(stored.SessionId.Value, "token_reuse", ct);
                await denylist.DenySessionAsync(stored.SessionId.Value, TimeSpan.FromMinutes(20), ct);
                await audit.AddAsync(
                    AuthAuditLog.Record(
                        stored.TenantId,
                        stored.UserId,
                        AuthAuditAction.TokenReuseDetected,
                        false,
                        request.IpAddress,
                        request.UserAgent,
                        correlation.CorrelationId,
                        targetType: "Session",
                        targetId: stored.SessionId
                    ),
                    ct
                );
                await bus.PublishAsync(
                    new SecurityAlertIntegrationEvent
                    {
                        TenantId = stored.TenantId,
                        UserId = stored.UserId,
                        AlertType = SecurityAlertType.TokenReuseDetected,
                        IpAddress = request.IpAddress,
                        UserAgent = request.UserAgent,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
                await unitOfWork.SaveChangesAsync(ct);
            }

            return Result.Failure<AuthTokensResponse>(invalid);
        }

        var session = await sessions.GetSessionByIdAsync(stored.SessionId.Value, ct);
        if (session is null || !session.IsActive)
            return Result.Failure<AuthTokensResponse>(invalid);

        var user = await users.GetByIdAsync(stored.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthTokensResponse>(invalid);

        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            return Result.Failure<AuthTokensResponse>(new Error("Tenant.Inactive", "Tenant is inactive."));
        }

        var (roleNames, permissions) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        var timeZone = UserAccessResolver.EffectiveTimeZone(user, tenant);

        var issued = await issuer.RotateAsync(stored, session, user, timeZone, roleNames, permissions, ["refresh"], ct);
        session.Touch(request.IpAddress);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.TokenRefreshed,
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

        return Result.Success(new AuthTokensResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds));
    }
}

public sealed record RevokeRefreshTokenCommand(string RefreshToken);

public static class RevokeRefreshTokenHandler
{
    public static async Task<Result> Handle(
        RevokeRefreshTokenCommand command,
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
        if (stored is null)
            return Result.Success();

        if (stored.SessionId is Guid sessionId)
        {
            await sessions.RevokeSessionAsync(sessionId, "user_logout", ct);
            await denylist.DenySessionAsync(sessionId, TimeSpan.FromMinutes(20), ct);
            await audit.AddAsync(
                AuthAuditLog.Record(
                    stored.TenantId,
                    stored.UserId,
                    AuthAuditAction.SessionRevoked,
                    true,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    targetType: "Session",
                    targetId: sessionId
                ),
                ct
            );
        }
        else
        {
            stored.Revoke("user_logout");
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
