namespace TaxVision.Auth.Infrastructure.Onboarding.Security;

/// <summary>
/// F25 — clave compuesta de <see cref="OnboardingServiceTokenCache"/>. Las colecciones
/// (<c>permissions</c>/<c>scopes</c>) se pre-unen en campos string canónicos porque la igualdad
/// de <c>record</c> en C# para miembros <see cref="IReadOnlyCollection{T}"/> es por referencia, no
/// estructural — con campos string la igualdad de <c>ServiceTokenCacheKey</c> es correcta y
/// reemplaza por completo el <c>Matches(...)</c> hecho a mano que tenía la versión anterior.
/// </summary>
internal sealed record ServiceTokenCacheKey(
    Guid TenantId,
    string ClientId,
    string PermissionsKey,
    string ScopesKey,
    string Audience,
    int LifetimeMinutes
)
{
    public static ServiceTokenCacheKey Create(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> scopes,
        string audience,
        int lifetimeMinutes
    ) => new(tenantId, clientId, string.Join('|', permissions), string.Join('|', scopes), audience, lifetimeMinutes);
}
