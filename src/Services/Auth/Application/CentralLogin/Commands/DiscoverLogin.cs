using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.CentralLogin.Commands;

public sealed record DiscoverLoginCommand(string Email, string Password, string? DeviceName = null);

/// <summary>Una oficina que el frontend puede ofrecer en el selector.</summary>
public sealed record DiscoverOfficeView(Guid TenantId, string Subdomain, string TenantName, bool MfaRequired);

/// <summary>
/// Respuesta polimórfica: o se resolvió a una sola oficina sin MFA y ya viaja el vale
/// (<see cref="Subdomain"/> + <see cref="Ticket"/>), o hace falta que el usuario elija/haga MFA
/// (<see cref="DiscoverySessionRef"/> + <see cref="Offices"/>). El frontend arma la URL de destino
/// (staff vs <c>/portal/client</c>) — Auth no conoce las rutas de la SPA.
/// </summary>
public sealed record DiscoverLoginResponse(
    string? Subdomain,
    Guid? Ticket,
    Guid? DiscoverySessionRef,
    IReadOnlyList<DiscoverOfficeView>? Offices
)
{
    public static DiscoverLoginResponse Direct(string subdomain, Guid ticket) => new(subdomain, ticket, null, null);

    public static DiscoverLoginResponse Selection(Guid sessionRef, IReadOnlyList<DiscoverOfficeView> offices) =>
        new(null, null, sessionRef, offices);
}

/// <summary>
/// Paso 1 del login central: autentica el password contra CADA oficina del email (los hashes son
/// por-tenant) y decide. 1 oficina sin MFA → emite el vale directo (menos saltos). Varias o con MFA
/// → guarda el set autenticado y devuelve el selector. Sin coincidencias → genérico (anti-enumeración).
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
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // 1. Throttle por IP: una vez por intento, NO por oficina candidata.
        if (await throttler.GetIpRetryAfterAsync(request.IpAddress, ct) is not null)
            return Result.Failure<DiscoverLoginResponse>(
                new Error("Auth.LockedOut", "Too many attempts. Try again later.")
            );

        var email = command.Email.Trim().ToLowerInvariant();
        var matches = await AuthenticateAcrossOfficesAsync(email, command.Password, users, tenants, hasher, mfa, ct);

        // 2. Sin coincidencias: fallo por IP + genérico. No se descuenta lockout por-cuenta (ver helper).
        if (matches.Count == 0)
            return await FailAsync(throttler, audit, request, correlation, unitOfWork, ct);

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
            return Result.Success(DiscoverLoginResponse.Direct(only.Subdomain, ticket));
        }

        // 4. Selección (varias oficinas, o MFA con método pendiente): guardar el set y devolver el
        //    selector. La vista marca MfaRequired solo cuando de verdad se pedirá un código.
        var sessionRef = await sessions.StoreAsync(
            new DiscoverySession(
                matches
                    .Select(m => new DiscoveredOffice(m.TenantId, m.UserId, m.ChallengeRequired, m.MustEnroll))
                    .ToList()
            ),
            ct
        );
        var offices = matches
            .Select(m => new DiscoverOfficeView(m.TenantId, m.Subdomain, m.TenantName, m.ChallengeRequired))
            .ToList();
        return Result.Success(DiscoverLoginResponse.Selection(sessionRef, offices));
    }

    private sealed record Match(
        Guid TenantId,
        Guid UserId,
        string Subdomain,
        string TenantName,
        bool ChallengeRequired,
        bool MustEnroll
    );

    /// <summary>
    /// Autentica el password contra el usuario de cada tenant donde el email tiene cuenta activa. Un
    /// password que no calza en una oficina NO descuenta el lockout de esa cuenta: como el mismo email
    /// puede tener distinta clave en cada oficina, hacerlo bloquearía oficinas ajenas al usar la clave
    /// de otra. El freno de fuerza bruta acá es el throttle por IP.
    /// </summary>
    private static async Task<List<Match>> AuthenticateAcrossOfficesAsync(
        string email,
        string password,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher hasher,
        IMfaRepository mfa,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var matches = new List<Match>();

        foreach (var tenantId in await users.GetActiveTenantIdsByEmailAsync(email, ct))
        {
            var user = await users.GetByEmailAsync(tenantId, email, ct);
            if (user is null || !user.IsActive || user.IsLockedOut(now))
                continue;
            if (!hasher.Verify(password, user.PasswordHash))
                continue;

            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null || !tenant.IsActive)
                continue;

            var mfa2 = await MfaRequirement.DisposeAsync(user, mfa, ct);
            matches.Add(
                new Match(tenantId, user.Id, tenant.SubDomain, tenant.Name, mfa2.ChallengeRequired, mfa2.MustEnroll)
            );
        }

        return matches;
    }

    private static async Task<Result<DiscoverLoginResponse>> FailAsync(
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await throttler.RegisterFailureAsync(request.IpAddress, ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                PlatformTenant.Id,
                null,
                AuthAuditAction.LoginFailed,
                false,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                detailsJson: """{"reason":"central_no_match"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<DiscoverLoginResponse>(new Error("Auth.Invalid", "Invalid credentials."));
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
