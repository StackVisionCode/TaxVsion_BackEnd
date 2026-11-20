using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.CentralLogin.Commands;

/// <summary>
/// Login de la app móvil del cliente sobre el host compartido de la API. Primera vuelta: email y contraseña,
/// devuelve las oficinas y una referencia efímera. Segunda vuelta: la referencia y la oficina elegida (más el
/// código si la oficina pide MFA). Si la app ya conoce la oficina, la manda en la primera vuelta.
/// </summary>
public sealed record MobileSignInCommand(
    string? Email = null,
    string? Password = null,
    Guid? TenantId = null,
    Guid? DiscoverySessionRef = null,
    string? MfaCode = null,
    bool RememberDevice = false,
    string? DeviceName = null,
    string? DeviceToken = null
);

public sealed record MobileOffice(Guid TenantId, string Name, string Subdomain, bool MfaRequired);

/// <summary>O el selector de oficinas, o los tokens de la sesión móvil, o el vale de takeover.</summary>
public sealed record MobileSignInResponse(
    bool RequiresTenantSelection,
    Guid? DiscoverySessionRef,
    IReadOnlyList<MobileOffice> Offices,
    string? AccessToken,
    string? RefreshToken,
    int? ExpiresInSeconds,
    bool MfaSetupRequired,
    bool TakeoverRequired,
    string? TakeoverTicket,
    int? TakeoverTicketExpiresInSeconds,
    string? DeviceToken
)
{
    public static MobileSignInResponse ForSelection(Guid reference, IReadOnlyList<MobileOffice> offices) =>
        new(true, reference, offices, null, null, null, false, false, null, null, null);

    public static MobileSignInResponse ForTokens(IssuedTokens tokens, bool mfaSetupRequired, string? deviceToken) =>
        new(
            false,
            null,
            [],
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresInSeconds,
            mfaSetupRequired,
            false,
            null,
            null,
            deviceToken
        );

    public static MobileSignInResponse ForTakeover(Guid ticket, bool mfaSetupRequired, string? deviceToken) =>
        new(
            false,
            null,
            [],
            null,
            null,
            null,
            mfaSetupRequired,
            true,
            ticket.ToString(),
            (int)LockoutPolicy.TakeoverTicketValidity.TotalSeconds,
            deviceToken
        );
}

/// <summary>
/// Caso de uso completo del login móvil. Comparte con el login web los frenos y la autenticación
/// (<see cref="CentralLoginAuthentication"/>), pero no salta de host: materializa la sesión aquí mismo, con
/// superficie <see cref="SessionSurface.Mobile"/>, sin vale intermedio. Solo cuentas del portal del cliente.
/// </summary>
public static class MobileSignInHandler
{
    private static readonly Error HandoffInvalid = new(
        "Auth.HandoffInvalid",
        "The sign-in session is invalid or has expired."
    );

    public static async Task<Result<MobileSignInResponse>> Handle(
        MobileSignInCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher hasher,
        IMfaRepository mfa,
        IDiscoverySessionStore discoverySessions,
        ITotpService totp,
        ISecretProtector protector,
        ISecureTokenService secureTokens,
        ILoginThrottler throttler,
        IRoleRepository roles,
        IAuthSessionIssuer issuer,
        ISessionRepository sessions,
        ISessionTakeoverTicketStore takeoverTickets,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IOptions<MfaOptions> mfaOptions,
        CancellationToken ct
    )
    {
        var reference = command.DiscoverySessionRef;

        // Primera vuelta: contraseña contra las oficinas del email, solo cuentas del portal.
        if (reference is null)
        {
            if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrWhiteSpace(command.Password))
                return Result.Failure<MobileSignInResponse>(
                    new Error("Auth.MobileCredentialsRequired", "Email and password are required.")
                );

            var discovered = await DiscoverAsync(
                command,
                users,
                tenants,
                hasher,
                mfa,
                discoverySessions,
                secureTokens,
                throttler,
                audit,
                request,
                correlation,
                unitOfWork,
                mfaOptions.Value.Enforced,
                ct
            );
            if (discovered.IsFailure)
                return Result.Failure<MobileSignInResponse>(discovered.Error);
            if (command.TenantId is null)
                return discovered;
            reference = discovered.Value.DiscoverySessionRef;
        }

        if (command.TenantId is not { } tenantId)
            return Result.Failure<MobileSignInResponse>(
                new Error("Auth.MobileTenantRequired", "TenantId is required to complete sign-in.")
            );

        return await CompleteAsync(
            command,
            reference!.Value,
            tenantId,
            users,
            tenants,
            mfa,
            discoverySessions,
            totp,
            protector,
            secureTokens,
            roles,
            issuer,
            sessions,
            takeoverTickets,
            audit,
            request,
            correlation,
            unitOfWork,
            ct
        );
    }

    private static async Task<Result<MobileSignInResponse>> DiscoverAsync(
        MobileSignInCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher hasher,
        IMfaRepository mfa,
        IDiscoverySessionStore discoverySessions,
        ISecureTokenService secureTokens,
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        bool mfaEnforced,
        CancellationToken ct
    )
    {
        var email = command.Email!.Trim().ToLowerInvariant();
        var gate = await CentralLoginAuthentication.CheckThrottleAsync(
            throttler,
            request,
            email,
            UserAccountKind.Portal,
            ct
        );
        if (gate.Throttled is { } throttled)
            return Result.Failure<MobileSignInResponse>(throttled);

        var (offices, platformAdminMatched) = await CentralLoginAuthentication.AuthenticateAsync(
            email,
            command.Password!,
            gate.Kinds,
            command.DeviceToken,
            SessionSurface.Mobile,
            users,
            tenants,
            hasher,
            mfa,
            secureTokens,
            mfaEnforced,
            ct
        );
        if (offices.Count == 0)
            return Result.Failure<MobileSignInResponse>(
                await CentralLoginAuthentication.FailAsync(
                    email,
                    gate.Kinds,
                    platformAdminMatched ? "mobile_platform_admin" : "mobile_no_match",
                    throttler,
                    audit,
                    request,
                    correlation,
                    unitOfWork,
                    ct
                )
            );

        await throttler.ResetEmailFailuresAsync(email, UserAccountKind.Portal, ct);

        // Siempre se devuelve el selector, aunque haya una oficina: la app guarda la elegida.
        var reference = await discoverySessions.StoreAsync(
            new DiscoverySession(
                offices
                    .Select(o => new DiscoveredOffice(
                        o.TenantId,
                        o.UserId,
                        o.ChallengeRequired,
                        o.MustEnroll,
                        o.AccountKind
                    ))
                    .ToList()
            ),
            ct
        );
        return Result.Success(
            MobileSignInResponse.ForSelection(
                reference,
                offices
                    .Select(o => new MobileOffice(o.TenantId, o.TenantName, o.Subdomain, o.ChallengeRequired))
                    .ToList()
            )
        );
    }

    private static async Task<Result<MobileSignInResponse>> CompleteAsync(
        MobileSignInCommand command,
        Guid reference,
        Guid tenantId,
        IUserRepository users,
        ITenantRegistry tenants,
        IMfaRepository mfa,
        IDiscoverySessionStore discoverySessions,
        ITotpService totp,
        ISecretProtector protector,
        ISecureTokenService secureTokens,
        IRoleRepository roles,
        IAuthSessionIssuer issuer,
        ISessionRepository sessions,
        ISessionTakeoverTicketStore takeoverTickets,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Peek, no consume: si el código falla, el cliente reintenta dentro de la ventana.
        var session = await discoverySessions.PeekAsync(reference, ct);
        // La oficina tiene que estar en el set que la contraseña ya validó, y ser una cuenta del portal.
        var office = session?.Offices.FirstOrDefault(o =>
            o.TenantId == tenantId && o.AccountKind == UserAccountKind.Portal
        );
        if (office is null)
            return Result.Failure<MobileSignInResponse>(HandoffInvalid);

        // El código tiene tope por cuenta: bloqueada, ni el código correcto pasa hasta que venza.
        var now = DateTime.UtcNow;
        var challenged = office.ChallengeRequired ? await users.GetByIdAsync(office.UserId, ct) : null;
        if (CentralMfaAttempts.Blocked(challenged, now) is { } lockedOut)
            return Result.Failure<MobileSignInResponse>(lockedOut);

        MfaCodeCheck? codeCheck = office.ChallengeRequired
            ? await MfaCodeVerifier.VerifyAsync(office.UserId, command.MfaCode, mfa, totp, protector, secureTokens, ct)
            : null;
        if (codeCheck == MfaCodeCheck.Invalid)
        {
            CentralMfaAttempts.RegisterFailure(challenged, now);
            await audit.AddAsync(
                Audit(office.TenantId, office.UserId, AuthAuditAction.MfaFailed, false, request, correlation, null),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<MobileSignInResponse>(
                new Error("Auth.MfaInvalid", "MFA code is invalid or expired.")
            );
        }

        CentralMfaAttempts.RegisterSuccess(challenged);

        // La referencia vive 60 s: tenant y usuario se revalidan antes de emitir.
        var tenant = await tenants.GetByIdAsync(office.TenantId, ct);
        var user = await users.GetByIdAsync(office.UserId, ct);
        if (
            tenant is not { IsActive: true }
            || user is null
            || user.TenantId != office.TenantId
            || !user.IsActive
            || user.ActorType != UserActorType.CustomerPortal
            || BillingAccessPolicy.IsBlockedForBilling(tenant, user.ActorType)
        )
            return Result.Failure<MobileSignInResponse>(HandoffInvalid);

        await discoverySessions.ConsumeAsync(reference, ct);

        // Un código de recuperación no marca el dispositivo: solo un TOTP real.
        var deviceToken =
            command.RememberDevice && codeCheck == MfaCodeCheck.Totp
                ? await TrustedDeviceIssuer.IssueAsync(
                    office.TenantId,
                    office.UserId,
                    mfa,
                    secureTokens,
                    audit,
                    request,
                    correlation,
                    ct
                )
                : null;

        string[] authMethods = codeCheck is null ? ["pwd"] : ["pwd", "otp"];
        var outcome = await SessionEstablishment.IssueOrRequireTakeoverAsync(
            user,
            tenant,
            authMethods,
            command.DeviceName,
            mustEnrollMfa: office.MustEnroll,
            SessionSurface.Mobile,
            roles,
            issuer,
            sessions,
            takeoverTickets,
            ct
        );

        await audit.AddAsync(
            Audit(
                user.TenantId,
                user.Id,
                AuthAuditAction.LoginSucceeded,
                true,
                request,
                correlation,
                outcome.TakeoverRequired ? """{"method":"mobile","takeoverRequired":true}""" : """{"method":"mobile"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            outcome.TakeoverRequired
                ? MobileSignInResponse.ForTakeover(outcome.TakeoverTicket!.Value, office.MustEnroll, deviceToken)
                : MobileSignInResponse.ForTokens(outcome.Tokens!, office.MustEnroll, deviceToken)
        );
    }

    private static AuthAuditLog Audit(
        Guid tenantId,
        Guid userId,
        string action,
        bool succeeded,
        IRequestContext request,
        ICorrelationContext correlation,
        string? detailsJson
    ) =>
        AuthAuditLog.Record(
            tenantId,
            userId,
            action,
            succeeded,
            request.IpAddress,
            request.UserAgent,
            correlation.CorrelationId,
            detailsJson: detailsJson
        );
}
