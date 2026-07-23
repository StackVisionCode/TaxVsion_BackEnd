using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.Web.Identity;

/// <summary>
/// RBAC Fase 9 (RBAC_Hardening_Plan.md) — consolida los 12 <c>TryGetTenantAndUser</c> privados
/// (algunos reparseando claims a mano con <c>User.FindFirst("tenant_id")?.Value</c> en vez de usar
/// <see cref="ClaimsPrincipalExtensions.TryGetTenantId"/>) y los 2 <c>TryResolveTenantId</c>
/// duplicados que ya vivían en <c>TenantBrandingController</c>/<c>Postmaster.ProvidersController</c>
/// con la misma lógica "PlatformAdmin puede operar sobre cualquier tenant, el resto solo sobre el
/// propio" copiada palabra por palabra.
/// </summary>
public static class ControllerIdentityExtensions
{
    public static bool TryGetTenantAndUser(this ControllerBase c, out Guid tenantId, out Guid userId)
    {
        tenantId = Guid.Empty;
        userId = Guid.Empty;
        if (!c.User.TryGetTenantId(out tenantId))
            return false;
        if (!c.User.TryGetUserId(out userId))
            return false;
        return true;
    }

    /// <summary>
    /// PlatformAdmin puede operar sobre cualquier tenant; el resto solo sobre el propio (claim
    /// <c>tenant_id</c> del JWT). Devuelve <c>false</c> si el <paramref name="requestedTenantId"/>
    /// no matchea al del token y el caller no es PlatformAdmin.
    /// </summary>
    public static bool TryResolveTenantId(this ControllerBase c, Guid requestedTenantId, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (!c.User.TryGetTenantId(out var tokenTenantId) && !c.User.IsPlatformAdmin())
            return false;

        if (c.User.IsPlatformAdmin() || requestedTenantId == tokenTenantId)
        {
            tenantId = requestedTenantId;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Variante con <paramref name="requestedTenantId"/> opcional — solo la necesitaba
    /// <c>Postmaster.ProvidersController.GetStatus</c> (query param opcional que, ausente, cae al
    /// tenant propio del token). Delega en el overload no-nullable de arriba para el resto de la
    /// lógica, así el "PlatformAdmin bypass + match contra el token" no queda duplicado dos veces.
    /// </summary>
    public static bool TryResolveTenantId(this ControllerBase c, Guid? requestedTenantId, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (!c.User.TryGetTenantId(out var tokenTenantId) && !c.User.IsPlatformAdmin())
            return false;

        if (requestedTenantId is null)
        {
            tenantId = tokenTenantId;
            return tenantId != Guid.Empty;
        }

        return c.TryResolveTenantId(requestedTenantId.Value, out tenantId);
    }
}
