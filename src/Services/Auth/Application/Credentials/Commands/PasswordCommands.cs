using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials.Commands;

// ---------------------------------------------------------------------------
// Forgot password
// ---------------------------------------------------------------------------

public sealed record ForgotPasswordCommand(Guid TenantId, string Email);

public sealed record ForgotPasswordCentralCommand(string Email);

public static class ForgotPasswordHandler
{
    private static readonly TimeSpan ResetValidity = TimeSpan.FromMinutes(30);

    /// <summary>Forgot password POR-TENANT (desde el subdominio de la oficina). Siempre devuelve éxito
    /// (anti-enumeración). El email solo se envía si el usuario existe.
    /// Fase 18 — throttle por email (3/hora) e IP (10/hora, cooldown 60s) antes de tocar la DB: un
    /// intento tirado por rate limit tampoco distingue email existente vs inexistente.</summary>
    public static async Task<Result> Handle(
        ForgotPasswordCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var email = command.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (await throttler.GetPasswordResetRetryAfterAsync(email, request.IpAddress, ct) is not null)
            return Result.Success();
        await throttler.RegisterPasswordResetRequestAsync(email, request.IpAddress, ct);

        var user = await users.GetByEmailAsync(command.TenantId, email, ct);
        if (user is null || !user.IsActive)
            return Result.Success();

        await IssueResetForUserAsync(user, credentials, tokens, audit, request, correlation, bus, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Emite el token de reset de un usuario, publica el evento (con su ActorType, para que
    /// el link vaya al portal o al CRM) y audita. NO guarda: el caller hace un único SaveChanges.
    /// Compartido por el forgot por-tenant y el central.</summary>
    internal static async Task IssueResetForUserAsync(
        User user,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var rawToken = tokens.GenerateToken();
        var resetToken = PasswordResetToken.Create(
            user.TenantId,
            user.Id,
            tokens.Hash(rawToken),
            request.IpAddress,
            ResetValidity
        );
        await credentials.AddPasswordResetAsync(resetToken, ct);

        await bus.PublishAsync(
            new PasswordResetRequestedIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                Email = user.Email,
                RawToken = rawToken,
                ExpiresAtUtc = resetToken.ExpiresAtUtc,
                ActorType = user.ActorType.ToString(),
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.PasswordResetRequested,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
    }
}

/// <summary>Forgot password CENTRAL (desde app.taxproffice.com, sin oficina en el Host). Descubre
/// TODAS las oficinas activas del email y emite un reset por cada una — cada link va a su subdominio,
/// al portal o al CRM según el actor de esa oficina. Siempre 202 (anti-enumeración): la respuesta no
/// revela cuántas oficinas hubo. El throttle por email+IP corre una sola vez, antes de tocar la DB.</summary>
public static class ForgotPasswordCentralHandler
{
    public static async Task<Result> Handle(
        ForgotPasswordCentralCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var email = command.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (await throttler.GetPasswordResetRetryAfterAsync(email, request.IpAddress, ct) is not null)
            return Result.Success();
        await throttler.RegisterPasswordResetRequestAsync(email, request.IpAddress, ct);

        var issued = false;
        foreach (var tenantId in await users.GetActiveTenantIdsByEmailAsync(email, ct))
        {
            var user = await users.GetByEmailAsync(tenantId, email, ct);
            if (user is null || !user.IsActive)
                continue;

            await ForgotPasswordHandler.IssueResetForUserAsync(
                user,
                credentials,
                tokens,
                audit,
                request,
                correlation,
                bus,
                ct
            );
            issued = true;
        }

        if (issued)
            await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Reset password (con token de un solo uso)
// ---------------------------------------------------------------------------

public sealed record ResetPasswordCommand(string Token, string NewPassword);

public static class ResetPasswordHandler
{
    public static async Task<Result> Handle(
        ResetPasswordCommand command,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IUserRepository users,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        IPasswordHasher hasher,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var invalid = new Error("Auth.InvalidResetToken", "Reset token is invalid or expired.");
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(invalid);

        var resetToken = await credentials.GetPasswordResetByHashAsync(tokens.Hash(command.Token), ct);
        if (resetToken is null || !resetToken.IsUsable(now))
            return Result.Failure(invalid);

        var user = await users.GetByIdAsync(resetToken.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(invalid);

        var policyResult = PasswordPolicy.Validate(command.NewPassword, user.Email);
        if (policyResult.IsFailure)
        {
            resetToken.RegisterAttempt();
            await unitOfWork.SaveChangesAsync(ct);
            return policyResult;
        }

        var changeResult = user.ChangePassword(hasher.Hash(command.NewPassword), now);
        if (changeResult.IsFailure)
        {
            resetToken.RegisterAttempt();
            await unitOfWork.SaveChangesAsync(ct);
            return changeResult;
        }

        resetToken.MarkUsed();
        user.RegisterSuccessfulLogin(); // limpia lockout previo

        // Cambio de contraseña ⇒ todas las sesiones anteriores dejan de ser válidas.
        var active = await sessions.GetActiveSessionsByUserAsync(user.Id, ct);
        foreach (var session in active)
            await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        await sessions.RevokeAllForUserAsync(user.Id, "password_change", null, ct);

        await bus.PublishAsync(
            new SecurityAlertIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                AlertType = SecurityAlertType.PasswordChanged,
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.PasswordResetCompleted,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Change password (autenticado)
// ---------------------------------------------------------------------------

public sealed record ChangePasswordCommand(Guid UserId, Guid SessionId, string CurrentPassword, string NewPassword);

public static class ChangePasswordHandler
{
    public static async Task<Result> Handle(
        ChangePasswordCommand command,
        IUserRepository users,
        IPasswordHasher hasher,
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
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(new Error("Auth.Invalid", "Invalid credentials."));

        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash))
            return Result.Failure(new Error("Auth.Invalid", "Invalid credentials."));

        var policyResult = PasswordPolicy.Validate(command.NewPassword, user.Email);
        if (policyResult.IsFailure)
            return policyResult;

        var changeResult = user.ChangePassword(hasher.Hash(command.NewPassword), DateTime.UtcNow);
        if (changeResult.IsFailure)
            return changeResult;

        // Revocar todas las sesiones excepto la actual.
        var active = await sessions.GetActiveSessionsByUserAsync(user.Id, ct);
        foreach (var session in active)
        {
            if (session.Id == command.SessionId)
                continue;
            await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        }
        await sessions.RevokeAllForUserAsync(user.Id, "password_change", command.SessionId, ct);

        await bus.PublishAsync(
            new SecurityAlertIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                AlertType = SecurityAlertType.PasswordChanged,
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.PasswordChanged,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
