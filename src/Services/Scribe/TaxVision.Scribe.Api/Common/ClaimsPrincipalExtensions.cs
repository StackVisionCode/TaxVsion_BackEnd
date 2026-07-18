using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TaxVision.Scribe.Api.Common;

/// <summary>
/// Lectura de identidad desde el JWT validado. El <c>tenant_id</c> siempre proviene del token,
/// nunca del cuerpo/query del cliente (aislamiento multitenant).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        var raw =
            principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }

    public static bool TryGetTenantId(this ClaimsPrincipal principal, out Guid tenantId) =>
        Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out tenantId);

    public static bool IsPlatformAdmin(this ClaimsPrincipal principal) => principal.IsInRole("PlatformAdmin");

    // TenantAdmin YA NO tiene bypass acá — depende del claim "perm" real (PermissionCatalog
    // computa su set completo al login, excluyendo lo marcado Permission.PlatformOnly).
    // PlatformAdmin sí lo conserva: por diseño debe pasar todo, sin depender de que el claim
    // esté correctamente poblado en cada caso borde (ver discusión de la Fase de auditoría).
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim("perm", permission) || principal.IsInRole("PlatformAdmin");
}
