using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users.Login;

/// <summary>Primer paso: frena la IP abusiva y encuentra la oficina y la cuenta.</summary>
public sealed class LoginDiscovery(
    ILoginThrottler throttler,
    ITenantRegistry tenants,
    IUserRepository users,
    AuthAuditTrail audit
)
{
    public async Task<(LoginAttempt? Attempt, LoginDecision? Refusal)> FindAsync(
        LoginCommand command,
        CancellationToken ct
    )
    {
        // Defensa complementaria al rate limiting del Gateway.
        var retryAfter = await throttler.GetIpRetryAfterAsync(audit.IpAddress, ct);
        if (retryAfter is not null)
        {
            return (
                null,
                LoginDecision.RefusedWithoutSaving(
                    new Error("Auth.LockedOut", "Too many attempts. Try again later.").WithRetryAfter(retryAfter.Value)
                )
            );
        }

        // Respuesta genérica hacia el anónimo (anti-enumeración); el motivo real queda solo en auditoría.
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return (null, await RefuseAnonymousAsync(command, """{"reason":"tenant_inactive_or_missing"}""", ct));

        var email = command.Email.Trim().ToLowerInvariant();
        var user = command.AccountKind is { } accountKind
            ? await users.GetByEmailAsync(command.TenantId, email, accountKind, ct)
            : await users.GetByEmailAsync(command.TenantId, email, UserAccountKind.Staff, ct)
                ?? await users.GetByEmailAsync(command.TenantId, email, UserAccountKind.Portal, ct);

        if (user is null)
            return (null, await RefuseAnonymousAsync(command, """{"reason":"unknown_email"}""", ct));

        return (new LoginAttempt(tenant, user, command.DeviceName), null);
    }

    private async Task<LoginDecision> RefuseAnonymousAsync(LoginCommand command, string detailsJson, CancellationToken ct)
    {
        await throttler.RegisterFailureAsync(audit.IpAddress, ct);
        await audit.RecordAsync(command.TenantId, null, AuthAuditAction.LoginFailed, false, ct, detailsJson: detailsJson);
        return LoginDecision.Refused(LoginPasswordCheck.InvalidCredentials);
    }
}
