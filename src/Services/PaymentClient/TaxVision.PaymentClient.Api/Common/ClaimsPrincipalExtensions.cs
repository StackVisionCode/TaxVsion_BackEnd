using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Lectura de identidad desde el JWT validado. El <c>tenant_id</c> siempre proviene del
/// token, nunca del cuerpo/query del cliente (aislamiento multitenant estricto).
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

    public static bool TryGetSessionId(this ClaimsPrincipal principal, out Guid sessionId)
    {
        var raw = principal.FindFirst("sid")?.Value ?? principal.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParse(raw, out sessionId);
    }

    public static bool IsPlatformAdmin(this ClaimsPrincipal principal) => principal.IsInRole("PlatformAdmin");

    // TenantAdmin ya no pasa por rol solo — debe traer el claim "perm" real, que Auth le
    // otorga por defecto vía PermissionCatalog.SystemRoleDefaults al emitir el JWT (salvo
    // permisos PlatformOnly, como AdminCrossTenant). Mismo hallazgo/fix que los otros 9
    // servicios (auditoría PlatformAdmin-only vs TenantAdmin, 2026-07-18) — el bypass de rol
    // dejaba pasar cualquier permiso, incluso uno 100% exclusivo de plataforma.
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim("perm", permission) || principal.IsInRole("PlatformAdmin");
}
