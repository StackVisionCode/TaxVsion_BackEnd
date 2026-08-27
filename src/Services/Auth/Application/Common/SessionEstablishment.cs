using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Punto único "usuario ya autenticado → sesión": resuelve los roles efectivos y la zona horaria del
/// tenant, y emite los tokens vía <see cref="IAuthSessionIssuer"/>. Lo comparten el login directo y el
/// canje del ticket de handoff cross-dominio, para que ambos deriven las mismas reclamaciones sin
/// duplicar las reglas de resolución.
/// </summary>
public static class SessionEstablishment
{
    public static async Task<IssuedTokens> IssueAsync(
        User user,
        Tenant tenant,
        IReadOnlyCollection<string> authMethods,
        string? deviceName,
        IRoleRepository roles,
        IAuthSessionIssuer issuer,
        CancellationToken ct
    )
    {
        var (roleNames, _) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        var timeZone = UserAccessResolver.EffectiveTimeZone(user, tenant);
        return await issuer.StartSessionAsync(user, timeZone, roleNames, authMethods, deviceName, ct);
    }

    /// <summary>
    /// Gate de SESIÓN ÚNICA. Si el usuario ya tiene alguna sesión activa, NO emite: devuelve un vale
    /// de takeover para que el frontend confirme (interstitial) y luego <c>POST /auth/session/takeover</c>
    /// revoque las anteriores y cree la nueva. Sin sesiones previas, emite normal. Debe pasar por acá
    /// cada punto que iba a mintear una sesión ya autenticada (login directo, verificación MFA, canje
    /// de handoff), para que el gate sea único y no se pueda saltar por una rama.
    /// </summary>
    public static async Task<SessionOutcome> IssueOrRequireTakeoverAsync(
        User user,
        Tenant tenant,
        IReadOnlyCollection<string> authMethods,
        string? deviceName,
        bool mustEnrollMfa,
        IRoleRepository roles,
        IAuthSessionIssuer issuer,
        ISessionRepository sessions,
        ISessionTakeoverTicketStore takeoverTickets,
        CancellationToken ct
    )
    {
        // IgnoreQueryFilters ya aplicado en el repo (guardrail #8): el login corre pre-JWT, sin tenant
        // en contexto, y el userId propio es la clave confiable.
        var active = await sessions.GetActiveSessionsByUserAsync(user.Id, ct);
        if (active.Count > 0)
        {
            var ticket = await takeoverTickets.IssueAsync(
                new SessionTakeoverPayload(user.TenantId, user.Id, [.. authMethods], deviceName, mustEnrollMfa),
                ct
            );
            return SessionOutcome.Takeover(ticket);
        }

        var issued = await IssueAsync(user, tenant, authMethods, deviceName, roles, issuer, ct);
        return SessionOutcome.Issued(issued);
    }
}

/// <summary>Desenlace del gate de sesión única: o se emitieron tokens, o hace falta confirmar el takeover.</summary>
public sealed record SessionOutcome
{
    private SessionOutcome() { }

    public IssuedTokens? Tokens { get; private init; }
    public Guid? TakeoverTicket { get; private init; }
    public bool TakeoverRequired => TakeoverTicket is not null;

    public static SessionOutcome Issued(IssuedTokens tokens) => new() { Tokens = tokens };

    public static SessionOutcome Takeover(Guid ticket) => new() { TakeoverTicket = ticket };
}
