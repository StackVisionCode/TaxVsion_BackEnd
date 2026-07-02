using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TaxVision.Auth.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        var raw = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                  principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }

    public static bool TryGetTenantId(this ClaimsPrincipal principal, out Guid tenantId)
        => Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out tenantId);

    public static bool TryGetSessionId(this ClaimsPrincipal principal, out Guid sessionId)
    {
        // JwtBearer puede mapear "sid" a ClaimTypes.Sid según el inbound claim map.
        var raw = principal.FindFirst("sid")?.Value ??
                  principal.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParse(raw, out sessionId);
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission)
        => principal.HasClaim("perm", permission) ||
           principal.IsInRole("TenantAdmin") ||
           principal.IsInRole("PlatformAdmin");
}
