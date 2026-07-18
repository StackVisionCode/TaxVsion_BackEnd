using System.Security.Claims;

namespace TaxVision.Customer.Api.Common;

/// <summary>
/// Lectura de identidad desde el JWT validado — mismo criterio que Signature/Notification/Auth.
/// PlatformAdmin pasa siempre; TenantAdmin depende del claim "perm" real (PermissionCatalog
/// computa su set completo al login, excluyendo lo marcado Permission.PlatformOnly) — el resto
/// también necesita el permiso explícito. El <c>tenant_id</c> siempre proviene del token (nunca
/// del query/body del cliente) — también es el claim que llevan los tokens M2M
/// (<c>actor_type=Service</c>), ver <c>ServiceOnly</c> policy.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim("perm", permission) || principal.IsInRole("PlatformAdmin");

    public static bool TryGetTenantId(this ClaimsPrincipal principal, out Guid tenantId) =>
        Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out tenantId);
}
