using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.CentralLogin;

/// <summary>Una oficina donde la contraseña del login central coincidió (los hashes son por tenant).</summary>
public sealed record AuthenticatedOffice(
    Guid TenantId,
    Guid UserId,
    string Subdomain,
    string TenantName,
    bool ChallengeRequired,
    bool MustEnroll,
    bool IsClientPortal
)
{
    public UserAccountKind AccountKind => IsClientPortal ? UserAccountKind.Portal : UserAccountKind.Staff;
}

/// <summary>
/// Tope de intentos del código MFA en el login central (web y móvil). La contraseña ya se validó para ESTE usuario,
/// así que cada código errado cuenta contra el bloqueo de su cuenta, igual que una contraseña errada en el login
/// clásico. Sin esto el código de 6 dígitos se podía probar sin límite por cuenta.
/// </summary>
public static class CentralMfaAttempts
{
    /// <summary>El error de espera si la cuenta está bloqueada; null si puede intentar.</summary>
    public static Error? Blocked(User? user, DateTime nowUtc) =>
        user is not null && user.IsLockedOut(nowUtc)
            ? new Error("Auth.LockedOut", "Too many attempts. Try again later.").WithRetryAfter(
                user.LockoutEndUtc!.Value - nowUtc
            )
            : null;

    public static void RegisterFailure(User? user, DateTime nowUtc) =>
        user?.RegisterFailedLogin(nowUtc, LockoutPolicy.MaxFailedAttempts, LockoutPolicy.LockoutDuration);

    /// <summary>Solo escribe si había fallos que limpiar.</summary>
    public static void RegisterSuccess(User? user)
    {
        if (user is { HasLoginFailures: true })
            user.RegisterSuccessfulLogin();
    }
}

/// <summary>Resultado del freno: el error si el intento debe esperar, o los tipos de cuenta que puede probar.</summary>
public sealed record LoginGate(Error? Throttled, IReadOnlyList<UserAccountKind> Kinds);

/// <summary>
/// Paso compartido del login central, web (<c>DiscoverLogin</c>) y móvil (<c>MobileSignIn</c>): los frenos por IP
/// y por email, la contraseña contra cada oficina del email y el fallo genérico. Un solo lugar para las reglas
/// de fuerza bruta: no se pueden saltar por una de las dos puertas.
/// </summary>
public static class CentralLoginAuthentication
{
    private static readonly Error LockedOut = new("Auth.LockedOut", "Too many attempts. Try again later.");

    /// <summary>
    /// Freno por IP y por email (y tipo de cuenta), una vez por intento y no por oficina candidata. Sin tipo de
    /// cuenta (login central del CRM) un tipo frenado se salta en vez de bloquear al otro: atacar la puerta del
    /// cliente (portal o app) no deja fuera al empleado con el mismo email, y la contraseña del tipo frenado
    /// tampoco se puede seguir probando por aquí. Espera solo si no queda ningún tipo por probar.
    /// </summary>
    public static async Task<LoginGate> CheckThrottleAsync(
        ILoginThrottler throttler,
        IRequestContext request,
        string email,
        UserAccountKind? accountKind,
        CancellationToken ct
    )
    {
        if (await throttler.GetIpRetryAfterAsync(request.IpAddress, ct) is { } ipRetry)
            return new LoginGate(LockedOut.WithRetryAfter(ipRetry), []);

        UserAccountKind[] asked = accountKind is { } only ? [only] : [UserAccountKind.Staff, UserAccountKind.Portal];
        var open = new List<UserAccountKind>();
        TimeSpan? shortestWait = null;
        foreach (var kind in asked)
        {
            if (await throttler.GetEmailRetryAfterAsync(email, kind, ct) is { } wait)
                shortestWait = shortestWait is { } current && current < wait ? current : wait;
            else
                open.Add(kind);
        }

        return open.Count == 0
            ? new LoginGate(LockedOut.WithRetryAfter(shortestWait!.Value), [])
            : new LoginGate(null, open);
    }

    /// <summary>
    /// Autentica la contraseña contra el usuario de cada tenant donde el email tiene cuenta activa. Un fallo en una
    /// oficina NO descuenta el lockout de esa cuenta: el mismo email puede tener otra clave en cada oficina, y
    /// hacerlo bloquearía oficinas ajenas. Los frenos aquí son los de IP y email.
    /// </summary>
    public static async Task<(IReadOnlyList<AuthenticatedOffice> Offices, bool PlatformAdminMatched)> AuthenticateAsync(
        string email,
        string password,
        IReadOnlyList<UserAccountKind> kinds,
        string? deviceToken,
        SessionSurface surface,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher hasher,
        IMfaRepository mfa,
        ISecureTokenService secureTokens,
        bool mfaEnforced,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var matches = new List<AuthenticatedOffice>();
        var platformAdminMatched = false;

        // Dispositivo de confianza: se resuelve UNA vez, no por oficina. El token identifica al
        // aparato y a un usuario concreto, así que solo exime del código a ESE usuario — tener el
        // equipo marcado para una cuenta no vale para otra que comparta el email.
        var trusted = string.IsNullOrWhiteSpace(deviceToken)
            ? null
            : await mfa.GetTrustedDeviceByHashAsync(secureTokens.Hash(deviceToken), ct);
        var trustedUserId = trusted is { IsActive: true } ? trusted.UserId : (Guid?)null;

        foreach (var kind in kinds)
        foreach (var tenantId in await users.GetActiveTenantIdsByEmailAsync(email, kind, ct))
        {
            var user = await users.GetByEmailAsync(tenantId, email, kind, ct);
            if (user is null || !user.IsActive || user.IsLockedOut(now))
                continue;
            if (!hasher.Verify(password, user.PasswordHash))
                continue;

            // El PlatformAdmin no entra por el login central: se descarta ANTES del MFA (antes se le pedía
            // el código y terminaba dentro del CRM de la oficina de plataforma).
            if (!CentralLoginActorPolicy.IsAllowed(user, surface))
            {
                platformAdminMatched = true;
                continue;
            }

            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null || !tenant.IsActive)
                continue;

            // Fase 2 — oficina con acceso bloqueado por facturación: no matchea para empleados/clientes
            // (el TenantAdmin sí, para poder entrar a renovar).
            if (BillingAccessPolicy.IsBlockedForBilling(tenant, user.ActorType))
                continue;

            // Landing (surface=Account) solo lista oficinas donde el usuario es TenantAdmin. Sin este
            // filtro, el flujo discover→selector→handoff→from-ticket terminaba con 403 "Only office
            // administrators..." al materializar la sesión — el usuario veía oficinas donde era
            // Employee/Portal como si pudiera entrar a gestionar suscripción, y recibía el rechazo
            // tres pasos después. AccountSurfacePolicy sigue siendo defensa en profundidad en
            // ExchangeHandoffTicket; este filtro corrige la raíz y evita la fuga de información.
            if (surface == SessionSurface.Account && user.ActorType != UserActorType.TenantAdmin)
                continue;

            var disposition = await MfaRequirement.DisposeAsync(user, mfa, ct, mfaEnforced);
            // El dispositivo de confianza exime del CÓDIGO, nunca del enrolamiento: si todavía no
            // tiene método, sigue teniendo que configurarlo.
            var challengeRequired = disposition.ChallengeRequired && trustedUserId != user.Id;
            matches.Add(
                new AuthenticatedOffice(
                    tenantId,
                    user.Id,
                    tenant.SubDomain,
                    tenant.Name,
                    challengeRequired,
                    disposition.MustEnroll,
                    user.ActorType == UserActorType.CustomerPortal
                )
            );
        }

        return (matches, platformAdminMatched);
    }

    /// <summary>
    /// Sin coincidencias: suma el fallo a la IP y al email, audita y responde genérico (anti-enumeración). Si la
    /// contraseña solo calzó en una cuenta PlatformAdmin la respuesta es idéntica; el motivo queda en la auditoría.
    /// </summary>
    public static async Task<Error> FailAsync(
        string email,
        IReadOnlyList<UserAccountKind> kinds,
        string reason,
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await throttler.RegisterFailureAsync(request.IpAddress, ct);
        foreach (var kind in kinds)
            await throttler.RegisterEmailFailureAsync(email, kind, ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                PlatformTenant.Id,
                null,
                AuthAuditAction.LoginFailed,
                false,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                detailsJson: $$"""{"reason":"{{reason}}"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return new Error("Auth.Invalid", "Invalid credentials.");
    }
}
