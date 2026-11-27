using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Application.Sessions;
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
        IAccountTokenProtector tokenProtector,
        IUserLoginLock userLock,
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IOptions<CredentialAdministrationOptions> credentialOptions,
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
                // D-08: durante la pausa tras un cambio de correo pedido por la oficina, el correo nuevo tampoco
                // resetea por su cuenta; si no, quien cambió el correo podría tomar la cuenta (03 §21).
                // Solo portal: el empleado conserva su forgot (IA-47).
                if (
                    kind == UserAccountKind.Portal
                    && OfficeCredentialAdministration.EmailChangeCooldownUntil(
                        user,
                        credentialOptions.Value,
                        DateTime.UtcNow
                    )
                        is not null
                )
                    continue;

                await PasswordResetIssuer.IssueAsync(
                    user,
                    ResetOrigin.SelfService,
                    null,
                    ResetValidity,
                    credentials,
                    tokens,
                    tokenProtector,
                    tenants,
                    userLock,
                    audit,
                    request,
                    correlation,
                    bus,
                    ct
                );
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
}

/// <summary>Cuando la contraseña de una cuenta cambia, sus enlaces de reset pendientes dejan de servir (OWASP).
/// Los de otras cuentas con el mismo email, por ejemplo en otra oficina, no se tocan.</summary>
internal static class PendingPasswordResets
{
    public static Task RevokeAsync(
        ICredentialTokenRepository credentials,
        Guid userId,
        DateTime utcNow,
        CancellationToken ct
    ) => RevokeAsync(credentials, userId, utcNow, ResetRevocationReason.PasswordChanged, null, ct);

    /// <summary>Con motivo: la baja de la cuenta los anula como <see cref="ResetRevocationReason.CancelledByOffice"/>
    /// para que no revivan al reactivarla.</summary>
    public static async Task RevokeAsync(
        ICredentialTokenRepository credentials,
        Guid userId,
        DateTime utcNow,
        ResetRevocationReason reason,
        Guid? byUserId,
        CancellationToken ct
    )
    {
        foreach (var token in await credentials.GetPendingPasswordResetsAsync(userId, utcNow, ct))
            token.Revoke(utcNow, reason, byUserId);
    }
}

/// <summary>
/// Reglas de una contraseña nueva elegida por el usuario (03 §10): la política con su contexto (nombre, oficina,
/// MFA para el mínimo de D-13) y distinta de la actual (<c>User.Password</c>).
/// </summary>
internal static class NewPasswordRules
{
    public static readonly Error SameAsCurrent = new(
        "User.Password",
        "Choose a password different from your current one."
    );

    public static async Task<Result> CheckAsync(
        User user,
        string? newPassword,
        IPasswordHasher hasher,
        ITenantRegistry tenants,
        CancellationToken ct
    )
    {
        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
        var context = new PasswordContext(
            user.Email,
            user.MfaEnabled,
            [user.Name, user.LastName, tenant?.Name ?? string.Empty, tenant?.SubDomain ?? string.Empty]
        );
        var policy = PasswordPolicy.Validate(newPassword, context);
        if (policy.IsFailure)
            return policy;

        return hasher.Verify(newPassword!, user.PasswordHash) ? Result.Failure(SameAsCurrent) : Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Reset password (con token de un solo uso)
// ---------------------------------------------------------------------------

/// <summary><see cref="HostTenantId"/>: la oficina que resolvió el Host de la request. Si está presente y es una
/// oficina de cliente, el token DEBE pertenecer a ella — un enlace de reset emitido para la oficina A no se
/// puede canjear desde el subdominio de la oficina B. El UI ya rebota al subdominio correcto en el load del
/// validate; este guard es la defensa en profundidad por si el UI falla o alguien llama la API directo.</summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword, Guid? HostTenantId = null);

public static class ResetPasswordHandler
{
    public static async Task<Result> Handle(
        ResetPasswordCommand command,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IUserRepository users,
        ITenantRegistry tenantRegistry,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        IPasswordHasher hasher,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IUserLoginLock userLock,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var found = await PasswordResetLinks.FindUsableAsync(command.Token, credentials, tokens, users, now, ct);
        if (found is not { } usable)
            return Result.Failure(PasswordResetLinks.Invalid);
        var (resetToken, user) = usable;

        if (
            await ResolveHostOfficeAsync(command.HostTenantId, tenantRegistry, ct) is { } hostOfficeId
            && hostOfficeId != resetToken.TenantId
        )
        {
            // Mismo código que un token no-usable: el canje falla y, por anti-enumeración, no decimos "a otra oficina".
            resetToken.RegisterAttempt();
            await unitOfWork.SaveChangesAsync(ct);
            await audit.AddAsync(
                AuthAuditLog.Record(
                    resetToken.TenantId,
                    user.Id,
                    AuthAuditAction.PasswordResetCompleted,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId
                ),
                ct
            );
            return Result.Failure(PasswordResetLinks.Invalid);
        }

        // 03 §18.4: canje y cancelación en fila con el candado del usuario; si el link ya no está pendiente
        // (cancelado o usado mientras esperaba), el error genérico. La consulta filtra en SQL: no ve el link en memoria.
        await userLock.AcquireAsync(user, ct);
        if (!(await credentials.GetPendingPasswordResetsAsync(user.Id, now, ct)).Any(link => link.Id == resetToken.Id))
            return Result.Failure(PasswordResetLinks.Invalid);

        var policyResult = await NewPasswordRules.CheckAsync(user, command.NewPassword, hasher, tenantRegistry, ct);
        if (policyResult.IsFailure)
        {
            resetToken.RegisterAttempt();
            await ConcurrentSave.TrySaveAsync(unitOfWork, ct);
            return policyResult;
        }

        // El link limpia cualquier estado de la credencial, también una cuenta asegurada (I-3).
        var changeResult = user.CompletePasswordReset(hasher.Hash(command.NewPassword), now);
        if (changeResult.IsFailure)
        {
            resetToken.RegisterAttempt();
            await ConcurrentSave.TrySaveAsync(unitOfWork, ct);
            return changeResult;
        }

        resetToken.MarkUsed();
        await PendingPasswordResets.RevokeAsync(credentials, user.Id, now, ct);
        user.RegisterSuccessfulLogin(); // limpia lockout previo

        // Cambio de contraseña ⇒ todas las sesiones dejan de valer, con aviso en tiempo real (I-6).
        var revoked = await cutoff.ForUserAsync(user, "password_change", ct);
        // D-05: el reset (cualquier origen) también olvida los dispositivos de confianza.
        foreach (var device in await mfa.GetTrustedDevicesAsync(user.Id, ct))
        {
            if (device.IsActive)
                device.Revoke();
        }

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.PasswordResetCompleted,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                detailsJson: $$"""{"origin":"{{resetToken.Origin}}"}"""
            ),
            ct
        );
        // Otro escritor tocó el link entre la lectura y el guardado: nada cambió y responde como token no válido.
        if (!await ConcurrentSave.TrySaveAsync(unitOfWork, ct))
            return Result.Failure(PasswordResetLinks.Invalid);
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
        if (revoked > 0)
            await bus.PublishAsync(
                SessionEndedEvents.ForUser(
                    user.TenantId,
                    user.Id,
                    null,
                    "password_change",
                    now,
                    correlation.CorrelationId
                )
            );
        return Result.Success();
    }

    /// <summary>El Host del tenant Platform (api.*) o uno no resuelto no acotan: el guard solo aplica cuando
    /// el Host es una oficina de cliente, igual que en <c>ForgotPasswordHandler.OfficeOfHostAsync</c>.</summary>
    private static async Task<Guid?> ResolveHostOfficeAsync(
        Guid? hostTenantId,
        ITenantRegistry tenantRegistry,
        CancellationToken ct
    )
    {
        if (hostTenantId is not { } tenantId)
            return null;
        var tenant = await tenantRegistry.GetByIdAsync(tenantId, ct);
        return tenant?.Kind == TenantKind.Customer ? tenant.Id : null;
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
        ITenantRegistry tenants,
        SessionAccessCutoff cutoff,
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

        // I-2: con la cuenta asegurada la contraseña actual ya no vale (solo un link la recupera).
        if (!hasher.Verify(command.CurrentPassword, user.PasswordHash) || user.EnsurePasswordSignInAllowed().IsFailure)
            return Result.Failure(new Error("Auth.Invalid", "Invalid credentials."));

        var policyResult = await NewPasswordRules.CheckAsync(user, command.NewPassword, hasher, tenants, ct);
        if (policyResult.IsFailure)
            return policyResult;

        var now = DateTime.UtcNow;
        var changeResult = user.ChangePassword(hasher.Hash(command.NewPassword), now);
        if (changeResult.IsFailure)
            return changeResult;

        await PendingPasswordResets.RevokeAsync(credentials, user.Id, now, ct);

        // Revocar todas las sesiones excepto la actual, con aviso en tiempo real (I-6).
        var revoked = await cutoff.ForUserAsync(user, "password_change", command.SessionId, ct);

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
        if (revoked > 0)
        {
            await bus.PublishAsync(
                SessionEndedEvents.ForUser(
                    user.TenantId,
                    user.Id,
                    command.SessionId,
                    "password_change",
                    now,
                    correlation.CorrelationId
                )
            );
        }
        return Result.Success();
    }
}
