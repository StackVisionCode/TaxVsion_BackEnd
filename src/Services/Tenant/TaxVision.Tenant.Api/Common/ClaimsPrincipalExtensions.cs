using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TaxVision.Tenant.Api.Common;

/// <summary>
/// Lectura de identidad y permisos desde el JWT validado — mismo criterio que
/// Postmaster/Signature/Notification/Customer. El <c>tenant_id</c> siempre proviene del token,
/// nunca del cuerpo/query/ruta del cliente (aislamiento multitenant estricto) — ver
/// TenantBrandingController.TryResolveTenantId.
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

    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim("perm", permission) || principal.IsInRole("PlatformAdmin");
}
