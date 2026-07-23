using System.Security.Claims;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Comportamiento actual (default, sin cambios) — mira únicamente lo que ya trae el JWT.
/// Nunca staleado hasta 15 min después de un cambio de permisos, pero no consulta nada externo.
/// </summary>
public sealed class JwtEmbeddedPermissionsSource : IUserPermissionsSource
{
    public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
        Task.FromResult(user.HasPermission(permission));
}
