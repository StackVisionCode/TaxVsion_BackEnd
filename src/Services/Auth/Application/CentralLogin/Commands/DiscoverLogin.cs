using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.CentralLogin.Commands;

/// <summary>
/// <see cref="AccountKind"/>: el login del portal pide solo cuentas Portal; sin indicarlo se autentican
/// ambas y una persona que es empleado y cliente de la misma oficina ve las dos entradas.
/// </summary>
/// <param name="DeviceToken">
/// Token de dispositivo de confianza guardado por este navegador en un login anterior. Si es válido
/// y pertenece al usuario, esa oficina deja de pedir el segundo factor.
/// </param>
public sealed record DiscoverLoginCommand(
    string Email,
    string Password,
    string? DeviceName = null,
    UserAccountKind? AccountKind = null,
    string? DeviceToken = null,
    SessionSurface Surface = SessionSurface.Workspace
);

/// <summary>
/// Una oficina que el frontend puede ofrecer en el selector. <see cref="IsClientPortal"/> le dice al
/// frontend a qué SPA mandar al usuario tras autenticar (cliente → /portal/client, staff → CRM).
/// </summary>
public sealed record DiscoverOfficeView(
    Guid TenantId,
    string Subdomain,
    string TenantName,
    bool MfaRequired,
    bool IsClientPortal
);

/// <summary>
/// Respuesta polimórfica: o se resolvió a una sola oficina sin MFA y ya viaja el vale
/// (<see cref="Subdomain"/> + <see cref="Ticket"/> + <see cref="IsClientPortal"/>), o hace falta que
/// el usuario elija/haga MFA (<see cref="DiscoverySessionRef"/> + <see cref="Offices"/>). El frontend
/// arma la URL de destino (staff vs <c>/portal/client</c>) según el actor — Auth no conoce las rutas.
/// </summary>
public sealed record DiscoverLoginResponse(
    string? Subdomain,
    Guid? Ticket,
    Guid? DiscoverySessionRef,
    IReadOnlyList<DiscoverOfficeView>? Offices,
    bool? IsClientPortal
)
{
    public static DiscoverLoginResponse Direct(string subdomain, Guid ticket, bool isClientPortal) =>
        new(subdomain, ticket, null, null, isClientPortal);

    public static DiscoverLoginResponse Selection(Guid sessionRef, IReadOnlyList<DiscoverOfficeView> offices) =>
        new(null, null, sessionRef, offices, null);
}

/// <summary>
/// Paso 1 del login central web: autentica el password contra CADA oficina del email (los hashes son
/// por-tenant) y decide. 1 oficina sin MFA → emite el vale directo (menos saltos). Varias o con MFA
/// → guarda el set autenticado y devuelve el selector. Sin coincidencias → genérico (anti-enumeración).
/// Los frenos y la autenticación los comparte con el login móvil (<see cref="CentralLoginAuthentication"/>).
/// </summary>
public static class DiscoverLoginHandler
{
    public static async Task<Result<DiscoverLoginResponse>> Handle(
        DiscoverLoginCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher hasher,
        IMfaRepository mfa,
        IDiscoverySessionStore sessions,
        IHandoffTicketStore tickets,
        ISecureTokenService secureTokens,
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IOptions<MfaOptions> mfaOptions,
        CancellationToken ct
    )
    {
        var email = command.Email.Trim().ToLowerInvariant();

        // 1. Frenos por IP y por email: una vez por intento, NO por oficina candidata.
        if (
            await CentralLoginAuthentication.CheckThrottleAsync(throttler, request, email, command.AccountKind, ct) is
            { } throttled
        )
            return Result.Failure<DiscoverLoginResponse>(throttled);

        var (matches, platformAdminMatched) = await CentralLoginAuthentication.AuthenticateAsync(
            email,
            command.Password,
            command.AccountKind,
            command.DeviceToken,
            command.Surface,
            users,
            tenants,
            hasher,
            mfa,
            secureTokens,
            mfaOptions.Value.Enforced,
            ct
        );

        // 2. Sin coincidencias: fallo para la IP y el email, y respuesta genérica.
        if (matches.Count == 0)
            return Result.Failure<DiscoverLoginResponse>(
                await CentralLoginAuthentication.FailAsync(
                    email,
                    command.AccountKind,
                    platformAdminMatched ? "central_platform_admin" : "central_no_match",
                    throttler,
                    audit,
                    request,
                    correlation,
                    unitOfWork,
                    ct
                )
            );

        await throttler.ResetEmailFailuresAsync(email, command.AccountKind, ct);

        // 3. Optimización: una sola oficina que no pide código (sin MFA, o MFA sin método enrolado)
        //    → el vale viaja directo, arrastrando el flag de "debe enrolar" si aplica.
        if (matches is [{ ChallengeRequired: false } only])
        {
            var ticket = await tickets.IssueAsync(
                new HandoffTicketPayload(only.TenantId, only.UserId, only.MustEnroll),
                ct
            );
            await audit.AddAsync(Success(only.TenantId, only.UserId, request, correlation, "discover_direct"), ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(DiscoverLoginResponse.Direct(only.Subdomain, ticket, only.IsClientPortal));
        }

        // 4. Selección (varias oficinas, o MFA con método pendiente): guardar el set y devolver el
        //    selector. La vista marca MfaRequired solo cuando de verdad se pedirá un código.
        var sessionRef = await sessions.StoreAsync(
            new DiscoverySession(
                matches
                    .Select(m => new DiscoveredOffice(
                        m.TenantId,
                        m.UserId,
                        m.ChallengeRequired,
                        m.MustEnroll,
                        m.AccountKind
                    ))
                    .ToList()
            ),
            ct
        );
        var offices = matches
            .Select(m => new DiscoverOfficeView(
                m.TenantId,
                m.Subdomain,
                m.TenantName,
                m.ChallengeRequired,
                m.IsClientPortal
            ))
            .ToList();
        return Result.Success(DiscoverLoginResponse.Selection(sessionRef, offices));
    }

    private static AuthAuditLog Success(
        Guid tenantId,
        Guid userId,
        IRequestContext request,
        ICorrelationContext correlation,
        string stage
    ) =>
        AuthAuditLog.Record(
            tenantId,
            userId,
            AuthAuditAction.LoginSucceeded,
            true,
            request.IpAddress,
            request.UserAgent,
            correlation.CorrelationId,
            detailsJson: $$"""{"stage":"{{stage}}"}"""
        );
}
