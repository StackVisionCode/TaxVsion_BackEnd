using BuildingBlocks.Permissions;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Auth es la única de las 14 microservicios que NO necesita una proyección local
/// eventualmente-consistente de permisos (RBAC Fase 7) — ya ES la fuente de verdad de
/// User/Role, así que puede resolver el snapshot en vivo contra sus propias tablas con el mismo
/// <see cref="UserAccessResolver"/> que usa al emitir el JWT en el login (ver
/// <c>Login.cs</c>/<c>AuthSessionIssuer</c>). Cero staleness posible: si <c>Authorization:PermissionsSource</c>
/// se activa acá, el "TokenStale" solo puede disparar por un JWT viejo, nunca por un
/// consumer de integration events atrasado (no hay ninguno en este servicio).
/// </summary>
public sealed class AuthUserPermissionsProjectionReader(IUserRepository users, IRoleRepository roles)
    : IUserPermissionsProjectionReader
{
    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId != tenantId)
            return null;

        var (_, permissions) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        return new UserPermissionsSnapshot(user.PermissionsVersion, permissions);
    }
}
