using System.Security.Claims;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Lectura tipada de los claims del JWT que emite Auth — reemplaza el parseo manual que estaba
/// duplicado (con variaciones reales entre servicios: algunos tenían <c>TryGetUserId</c> o
/// <c>IsPlatformAdmin</c>, otros no) en cada uno de los 11 microservicios "estándar" (ver Fase 1
/// y Fase 3 de Actor_Type_Authorization_Layers_Plan.md). <c>HasPermission</c> es la versión que
/// ya coincidía en 11 de esas 12 copias (con bypass de <c>PlatformAdmin</c>) — Growth usa
/// deliberadamente un modelo distinto (scopes M2M, sin este bypass) y queda fuera de esta
/// consolidación, ver Fase 4.
/// <para>
/// El <c>tenant_id</c> siempre proviene del token, nunca del cuerpo/query/ruta del cliente
/// (aislamiento multitenant estricto) — el mismo criterio que ya documentaban todas las copias.
/// </para>
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        // "sub" es el claim JWT estándar (RFC 7519) — se lee el literal para no traer la
        // dependencia de System.IdentityModel.Tokens.Jwt a este proyecto base, que
        // deliberadamente no referencia el stack de ASP.NET Core (eso vive en BuildingBlocks.Web).
        var raw = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }

    public static bool TryGetTenantId(this ClaimsPrincipal principal, out Guid tenantId) =>
        Guid.TryParse(principal.FindFirst(ClaimNames.TenantId)?.Value, out tenantId);

    /// <summary>Solo presente para <c>CustomerPortal</c> — ver <see cref="ActorType.CustomerPortal"/>.</summary>
    public static bool TryGetCustomerId(this ClaimsPrincipal principal, out Guid customerId) =>
        Guid.TryParse(principal.FindFirst(ClaimNames.CustomerId)?.Value, out customerId);

    public static bool TryGetSessionId(this ClaimsPrincipal principal, out Guid sessionId)
    {
        // JwtBearer puede mapear "sid" a ClaimTypes.Sid según el inbound claim map.
        var raw = principal.FindFirst("sid")?.Value ?? principal.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParse(raw, out sessionId);
    }

    public static bool IsPlatformAdmin(this ClaimsPrincipal principal) => principal.IsInRole("PlatformAdmin");

    // TenantAdmin no tiene bypass acá — depende del claim "perm" real (PermissionCatalog computa
    // su set completo al login, excluyendo lo marcado Permission.PlatformOnly). PlatformAdmin sí
    // lo conserva: por diseño debe pasar todo, sin depender de que el claim esté correctamente
    // poblado en cada caso borde (mismo criterio ya documentado en las 11 copias originales).
    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.HasClaim(ClaimNames.Permission, permission) || principal.IsPlatformAdmin();

    /// <summary>Null si el claim falta o trae un valor que no matchea ningún <see cref="ActorType"/>
    /// conocido — se trata como "no confiable", nunca se asume un actor type por default
    /// (fail-closed, ver Fase 0 de Actor_Type_Authorization_Layers_Plan.md).</summary>
    public static ActorType? GetActorType(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(ClaimNames.ActorType)?.Value;
        return Enum.TryParse<ActorType>(raw, ignoreCase: false, out var actorType) ? actorType : null;
    }
}
