using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

/// <summary>Usuario, sesión y superficie salen del token del llamador; la contraseña (y el código) del body.</summary>
public sealed record ReauthenticateCommand(
    Guid UserId,
    Guid SessionId,
    SessionSurface Surface,
    string Password,
    string? MfaCode = null
);

public sealed record ReauthenticateResponse(string AccessToken, int ExpiresInSeconds, DateTime ReauthenticatedAtUtc);

/// <summary>
/// Step-up: confirma la contraseña (y el TOTP si el usuario lo tiene) y emite un access token del MISMO
/// <c>sid</c> y superficie con <c>reauth_at</c>. El refresh no lo copia, así que la elevación dura como mucho
/// lo que el access token. Los fallos cuentan para el bloqueo de la cuenta, igual que en el login.
/// </summary>
public static class ReauthenticateHandler
{
    public static readonly Error Failed = new(
        "Auth.ReauthenticationFailed",
        "That password or code isn't right. Try again."
    );

    public static readonly Error CodeRequired = new(
        "Auth.MfaCodeRequired",
        "Enter the code from your authenticator app."
    );

    public static async Task<Result<ReauthenticateResponse>> Handle(
        ReauthenticateCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        ISessionRepository sessions,
        IRoleRepository roles,
        IPasswordHasher hasher,
        IMfaRepository mfa,
        ITotpService totp,
        ISecretProtector protector,
        ISecureTokenService tokens,
        IJwtTokenGenerator jwt,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var sessionEnded = new Error("Auth.SessionRevoked", "Session is no longer active.");

        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<ReauthenticateResponse>(sessionEnded);

        var session = await sessions.GetSessionByIdAsync(command.SessionId, ct);
        if (session is null || !session.IsActive || session.UserId != user.Id)
            return Result.Failure<ReauthenticateResponse>(sessionEnded);

        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<ReauthenticateResponse>(new Error("Tenant.Inactive", "Tenant is inactive."));

        if (user.IsLockedOut(now))
        {
            return Result.Failure<ReauthenticateResponse>(
                new Error("Auth.LockedOut", "Account is temporarily locked. Try again later.").WithRetryAfter(
                    user.LockoutEndUtc!.Value - now
                )
            );
        }

        if (!hasher.Verify(command.Password, user.PasswordHash))
            return await FailAsync(user, "bad_password", audit, request, correlation, unitOfWork, bus, now, ct);

        string[] authMethods = ["pwd"];
        if (await MfaCodeVerifier.HasConfirmedTotpAsync(user.Id, mfa, ct))
        {
            // Sin código no se cuenta como fallo: el cliente todavía no mostró el campo.
            if (string.IsNullOrWhiteSpace(command.MfaCode))
                return Result.Failure<ReauthenticateResponse>(CodeRequired);

            var check = await MfaCodeVerifier.VerifyAsync(user.Id, command.MfaCode, mfa, totp, protector, tokens, ct);
            if (check == MfaCodeCheck.Invalid)
                return await FailAsync(user, "bad_mfa_code", audit, request, correlation, unitOfWork, bus, now, ct);

            authMethods = ["pwd", check == MfaCodeCheck.Totp ? "totp" : "recovery"];
        }

        user.RegisterSuccessfulLogin();

        var (roleNames, _) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        var timeZone = UserAccessResolver.EffectiveTimeZone(user, tenant);
        var accessToken = jwt.Generate(user, timeZone, session.Id, roleNames, authMethods, command.Surface, now);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.Reauthenticated,
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

        return Result.Success(new ReauthenticateResponse(accessToken.Token, accessToken.ExpiresInSeconds, now));
    }

    private static async Task<Result<ReauthenticateResponse>> FailAsync(
        User user,
        string reason,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        DateTime now,
        CancellationToken ct
    )
    {
        user.RegisterFailedLogin(now, LockoutPolicy.MaxFailedAttempts, LockoutPolicy.LockoutDuration);
        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.ReauthenticationFailed,
                false,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                detailsJson: $$"""{"reason":"{{reason}}"}"""
            ),
            ct
        );

        if (user.IsLockedOut(now))
        {
            await bus.PublishAsync(
                new SecurityAlertIntegrationEvent
                {
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    AlertType = SecurityAlertType.AccountLockedOut,
                    IpAddress = request.IpAddress,
                    UserAgent = request.UserAgent,
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<ReauthenticateResponse>(Failed);
    }
}
