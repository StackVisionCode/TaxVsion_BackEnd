using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users.Login;

/// <summary>Segundo paso: bloqueo por cuenta, contraseña y si la cuenta y la oficina dejan entrar.</summary>
public sealed class LoginPasswordCheck(
    IPasswordHasher hasher,
    IUserLoginLock loginLock,
    ILoginThrottler throttler,
    AuthAuditTrail audit
)
{
    public static readonly Error InvalidCredentials = new("Auth.Invalid", "Invalid credentials.");

    public static readonly Error PortalAccountInactive = new(
        "Auth.UserInactive",
        "Your account with this office is no longer active. Please contact your office."
    );

    /// <summary>Devuelve el rechazo, o null si la contraseña vale y el login sigue.</summary>
    public async Task<LoginDecision?> VerifyAsync(LoginAttempt attempt, string password, DateTime now, CancellationToken ct)
    {
        var user = attempt.User;

        // Lockout autoritativo por cuenta.
        if (user.IsLockedOut(now))
        {
            await audit.RecordAsync(user.TenantId, user.Id, AuthAuditAction.LoginLockedOut, false, ct);
            return LoginDecision.Refused(
                new Error("Auth.LockedOut", "Account is temporarily locked. Try again later.").WithRetryAfter(
                    user.LockoutEndUtc!.Value - now
                )
            );
        }

        if (!hasher.Verify(password, user.PasswordHash))
            return await RefuseBadPasswordAsync(user, now, ct);

        // Con la contraseña ya validada no hay enumeración: el portal recibe el motivo para avisar al cliente.
        if (!user.IsActive)
        {
            return await RefuseAsync(
                user,
                """{"reason":"user_inactive"}""",
                user.AccountKind == UserAccountKind.Portal ? PortalAccountInactive : InvalidCredentials,
                ct
            );
        }

        // Fase 2 — corte de acceso por facturación: la suscripción del tenant está Suspended/Expired.
        // Se bloquea a empleados y clientes; el TenantAdmin/PlatformAdmin pasa para poder ir a renovar.
        if (BillingAccessPolicy.IsBlockedForBilling(attempt.Tenant, user.ActorType))
        {
            return await RefuseAsync(
                user,
                """{"reason":"subscription_inactive"}""",
                new Error(
                    "Auth.SubscriptionInactive",
                    "This office's subscription is inactive. Please renew to restore access."
                ),
                ct
            );
        }

        if (user.HasLoginFailures)
            await loginLock.AcquireAsync(user, ct);
        user.RegisterSuccessfulLogin();
        return null;
    }

    private async Task<LoginDecision> RefuseBadPasswordAsync(User user, DateTime now, CancellationToken ct)
    {
        await loginLock.AcquireAsync(user, ct);
        var alreadyLocked = user.IsLockedOut(now);
        if (!alreadyLocked)
            user.RegisterFailedLogin(now, LockoutPolicy.MaxFailedAttempts, LockoutPolicy.LockoutDuration);
        await throttler.RegisterFailureAsync(audit.IpAddress, ct);
        await audit.RecordAsync(
            user.TenantId,
            user.Id,
            AuthAuditAction.LoginFailed,
            false,
            ct,
            detailsJson: """{"reason":"bad_password"}"""
        );

        var justLocked = !alreadyLocked && user.IsLockedOut(now);
        return LoginDecision.Refused(InvalidCredentials, justLocked ? LockedOutAlert(user) : null);
    }

    private async Task<LoginDecision> RefuseAsync(User user, string detailsJson, Error error, CancellationToken ct)
    {
        await audit.RecordAsync(user.TenantId, user.Id, AuthAuditAction.LoginFailed, false, ct, detailsJson: detailsJson);
        return LoginDecision.Refused(error);
    }

    private SecurityAlertIntegrationEvent LockedOutAlert(User user) =>
        new()
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            AlertType = SecurityAlertType.AccountLockedOut,
            IpAddress = audit.IpAddress,
            UserAgent = audit.UserAgent,
            CorrelationId = audit.CorrelationId,
        };
}
