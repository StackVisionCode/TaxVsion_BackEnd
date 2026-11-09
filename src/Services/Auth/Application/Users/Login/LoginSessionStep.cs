using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;

namespace TaxVision.Auth.Application.Users.Login;

/// <summary>Por dónde llegó el login a abrir sesión: cambia lo auditado y si el enrolamiento MFA queda pendiente.</summary>
public enum LoginSessionEntry
{
    Password,
    MfaSetupPending,
    TrustedDevice,
}

/// <summary>Último paso: sesión única. Con una sesión activa exige takeover; si no, emite directo.</summary>
public sealed class LoginSessionStep(
    IRoleRepository roles,
    IAuthSessionIssuer issuer,
    ISessionRepository sessions,
    ISessionTakeoverTicketStore takeoverTickets,
    AuthAuditTrail audit
)
{
    public async Task<LoginDecision> OpenAsync(LoginAttempt attempt, LoginSessionEntry entry, CancellationToken ct)
    {
        var user = attempt.User;
        var mustEnrollMfa = entry == LoginSessionEntry.MfaSetupPending;
        var outcome = await SessionEstablishment.IssueOrRequireTakeoverAsync(
            user,
            attempt.Tenant,
            ["pwd"],
            attempt.DeviceName,
            mustEnrollMfa,
            SessionSurface.Workspace,
            roles,
            issuer,
            sessions,
            takeoverTickets,
            ct
        );

        await audit.RecordAsync(
            user.TenantId,
            user.Id,
            AuthAuditAction.LoginSucceeded,
            true,
            ct,
            detailsJson: AuditDetails(entry, outcome.TakeoverRequired)
        );

        if (outcome.TakeoverRequired)
        {
            return LoginDecision.Answered(
                LoginResponse.ForTakeover(
                    outcome.TakeoverTicket!.Value.ToString(),
                    (int)LockoutPolicy.TakeoverTicketValidity.TotalSeconds,
                    mfaSetupRequired: mustEnrollMfa,
                    actorType: user.ActorType.ToString()
                )
            );
        }

        var issued = outcome.Tokens!;
        return LoginDecision.Answered(
            LoginResponse.ForTokens(
                new AuthTokensResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds),
                mfaSetupRequired: mustEnrollMfa,
                actorType: user.ActorType.ToString()
            )
        );
    }

    private static string? AuditDetails(LoginSessionEntry entry, bool takeoverRequired) =>
        (entry, takeoverRequired) switch
        {
            (LoginSessionEntry.MfaSetupPending, true) => """{"takeoverRequired":true,"mfaSetupRequired":true}""",
            (LoginSessionEntry.MfaSetupPending, false) => """{"mfaSetupRequired":true}""",
            (LoginSessionEntry.TrustedDevice, true) => """{"takeoverRequired":true,"trustedDevice":true}""",
            (LoginSessionEntry.TrustedDevice, false) => """{"trustedDevice":true}""",
            (_, true) => """{"takeoverRequired":true}""",
            _ => null,
        };
}
