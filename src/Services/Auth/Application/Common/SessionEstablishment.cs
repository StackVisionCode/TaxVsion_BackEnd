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
}
