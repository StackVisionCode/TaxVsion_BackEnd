using System.Security.Claims;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Fuente de verdad para "¿este usuario tiene este permiso, ahora mismo?" — <see cref="PermissionPolicyProvider"/>
/// (RBAC Fase 7) resuelve una de estas dos implementaciones por servicio vía
/// <c>Authorization:PermissionsSource</c> ("Jwt" default | "Projection"). Ninguna otra clase debe
/// leer el claim <c>perm</c> directamente para decidir autorización — así el día que Fase 7.5
/// deje de embeber <c>perm</c> en el JWT, solo esta interfaz cambia de implementación default.
/// </summary>
public interface IUserPermissionsSource
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default);
}
