using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TaxVision.Notification.Api.Common;

/// <summary>
/// Lectura de identidad desde el JWT validado. El <c>tenant_id</c> siempre proviene del token,
/// nunca del cuerpo/query del cliente (aislamiento multitenant).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        // El handler JWT mapea "sub" a ClaimTypes.NameIdentifier; se leen ambos por seguridad.
        var raw =
            principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }

    public static bool TryGetTenantId(this ClaimsPrincipal principal, out Guid tenantId) =>
        Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out tenantId);

    public static bool IsPlatformAdmin(this ClaimsPrincipal principal) => principal.IsInRole("PlatformAdmin");

    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim("perm", permission)
        || principal.IsInRole("TenantAdmin")
        || principal.IsInRole("PlatformAdmin");
}
