using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials.Commands;

// ---------------------------------------------------------------------------
// Forgot password
// ---------------------------------------------------------------------------

/// <summary><see cref="AccountKind"/>: de qué cuenta se pide el reset (CRM → Staff, portal → Portal); sin
/// indicarlo, de todas. <see cref="HostTenantId"/>: la oficina que resolvió el Host de la request.</summary>
public sealed record ForgotPasswordCommand(
    string Email,
    UserAccountKind? AccountKind = null,
    Guid? HostTenantId = null
);

/// <summary>Desde el subdominio de una oficina el reset es solo de esa oficina; desde la entrada general
/// (app.*, api.*, localhost) se emite uno por cada oficina del email. Siempre éxito (anti-enumeración). El
/// throttle por email+IP corre una sola vez, antes de tocar la DB.</summary>
public static class ForgotPasswordHandler
{
    private static readonly TimeSpan ResetValidity = TimeSpan.FromMinutes(30);

    public static async Task<Result> Handle(
        ForgotPasswordCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
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

        var office = await OfficeOfHostAsync(command.HostTenantId, tenants, ct);
        var issued = false;
        UserAccountKind[] kinds = command.AccountKind is { } only
            ? [only]
            : [UserAccountKind.Staff, UserAccountKind.Portal];
        foreach (var kind in kinds)
        {
            IReadOnlyList<Guid> tenantIds = office is { } officeId
                ? [officeId]
                : await users.GetActiveTenantIdsByEmailAsync(email, kind, ct);
            foreach (var tenantId in tenantIds)
            {
                var user = await users.GetByEmailAsync(tenantId, email, kind, ct);
                if (user is null || !user.IsActive)
                    continue;

                await IssueResetForUserAsync(user, tenants, credentials, tokens, audit, request, correlation, bus, ct);
                issued = true;
            }
        }

        if (issued)
            await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Solo una oficina de cliente acota el reset. El Host del tenant Platform (api.*) es entrada
    /// general, igual que un Host que no resolvió.</summary>
    private static async Task<Guid?> OfficeOfHostAsync(
        Guid? hostTenantId,
        ITenantRegistry tenants,
        CancellationToken ct
    )
    {
        if (hostTenantId is not { } tenantId)
            return null;

        var tenant = await tenants.GetByIdAsync(tenantId, ct);
        return tenant?.Kind == TenantKind.Customer ? tenant.Id : null;
    }

    /// <summary>Emite el token de reset del usuario, publica el evento (con su ActorType y oficina, para que
    /// el correo enlace a su superficie) y audita. No guarda: Handle hace un único SaveChanges.</summary>
    private static async Task IssueResetForUserAsync(
        User user,
        ITenantRegistry tenants,
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
        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);

        await bus.PublishAsync(
            new PasswordResetRequestedIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                Email = user.Email,
                RawToken = rawToken,
                ExpiresAtUtc = resetToken.ExpiresAtUtc,
                ActorType = user.ActorType.ToString(),
                TenantName = tenant?.Name,
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

/// <summary>Cuando la contraseña de una cuenta cambia, sus enlaces de reset pendientes dejan de servir (OWASP).
/// Los de otras cuentas con el mismo email, por ejemplo en otra oficina, no se tocan.</summary>
internal static class PendingPasswordResets
{
    public static async Task RevokeAsync(
        ICredentialTokenRepository credentials,
        Guid userId,
        DateTime utcNow,
        CancellationToken ct
    )
    {
        foreach (var token in await credentials.GetPendingPasswordResetsAsync(userId, utcNow, ct))
            token.Revoke(utcNow);
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
        var now = DateTime.UtcNow;
        var found = await PasswordResetLinks.FindUsableAsync(command.Token, credentials, tokens, users, now, ct);
        if (found is not { } usable)
            return Result.Failure(PasswordResetLinks.Invalid);
        var (resetToken, user) = usable;

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
        await PendingPasswordResets.RevokeAsync(credentials, user.Id, now, ct);
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
        ICredentialTokenRepository credentials,
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

        var now = DateTime.UtcNow;
        var changeResult = user.ChangePassword(hasher.Hash(command.NewPassword), now);
        if (changeResult.IsFailure)
            return changeResult;

        await PendingPasswordResets.RevokeAsync(credentials, user.Id, now, ct);

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
