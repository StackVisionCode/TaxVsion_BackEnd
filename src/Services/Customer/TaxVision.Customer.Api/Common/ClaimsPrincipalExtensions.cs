using System.Security.Claims;

namespace TaxVision.Customer.Api.Common;

/// <summary>
/// Lectura del claim "perm" del JWT validado — mismo criterio que Signature/Notification/Auth.
/// TenantAdmin y PlatformAdmin pasan siempre; el resto necesita el permiso explícito.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim("perm", permission)
        || principal.IsInRole("TenantAdmin")
        || principal.IsInRole("PlatformAdmin");
}
