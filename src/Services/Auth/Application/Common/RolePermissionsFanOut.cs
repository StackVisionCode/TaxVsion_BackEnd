using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Avisa por titular cuando el acceso de un usuario cambió sin que nadie le tocara sus roles:
/// cambiaron los permisos de un rol que tiene, el rol se desactivó, o un deny temporal venció.
/// Publica <see cref="UserRolesChangedIntegrationEvent"/> para cada uno, con sus códigos
/// <b>efectivos</b> —la unión de todos sus roles activos menos sus denies vigentes— y su
/// <c>PermissionsVersion</c> ya subida.
/// <para>
/// Sin esto, la única señal era <see cref="RolePermissionsChangedIntegrationEvent"/>, que solo dice
/// qué permisos tiene el rol. Cada proyección local recomponía la unión de los roles del usuario a
/// partir de ahí, y como la capa de denies vive solo en Auth, el permiso denegado volvía a aparecer.
/// </para>
/// <para>
/// Subir <c>PermissionsVersion</c> es parte del contrato: las proyecciones aplican el evento solo si
/// es más nuevo (<c>ApplyIfNewer</c>), y el JWT del usuario queda desactualizado a propósito — el
/// siguiente request responde <c>401 Auth.TokenStale</c> y el frontend refresca.
/// </para>
/// </summary>
public static class RolePermissionsFanOut
{
    /// <summary>
    /// Tres consultas en total (titulares, sus roles, sus denies), no una por titular: esto corre
    /// también en el resync de arranque, que recorre los roles de sistema de todos los tenants.
    /// El caller es responsable de guardar: acá solo se muta <c>PermissionsVersion</c> y se publica.
    /// </summary>
    public static async Task PublishForRoleHoldersAsync(
        Guid tenantId,
        Guid roleId,
        IReadOnlyList<Permission> catalog,
        IUserRepository users,
        IRoleRepository roles,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct
    )
    {
        var holders = await users.GetActiveByRoleAsync(tenantId, roleId, ct);
        await PublishForUsersAsync(holders, catalog, roles, bus, correlationId, ct);
    }

    /// <summary>
    /// Igual que el anterior pero para un conjunto de usuarios ya resuelto, que puede cruzar tenants
    /// (el tenant sale de cada usuario). Lo usa el job que limpia los denies vencidos.
    /// </summary>
    public static async Task PublishForUsersAsync(
        IReadOnlyList<User> holders,
        IReadOnlyList<Permission> catalog,
        IRoleRepository roles,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct
    )
    {
        if (holders.Count == 0)
            return;

        var holderIds = holders.Select(holder => holder.Id).ToList();
        var rolesByUser = await roles.GetRolesByUsersAsync(holderIds, ct);
        var deniesByUser = await roles.GetDeniedPermissionIdsByUsersAsync(holderIds, ct);

        foreach (var holder in holders)
        {
            var activeRoles = rolesByUser.TryGetValue(holder.Id, out var assigned)
                ? assigned.Where(role => role.IsActive).ToList()
                : [];
            var denied = deniesByUser.TryGetValue(holder.Id, out var denies) ? denies : [];

            holder.BumpPermissionsVersion();
            await bus.PublishAsync(
                new UserRolesChangedIntegrationEvent
                {
                    TenantId = holder.TenantId,
                    UserId = holder.Id,
                    PermissionsVersion = holder.PermissionsVersion,
                    RoleNames = activeRoles.Select(role => role.Name).ToArray(),
                    RoleIds = activeRoles.Select(role => role.Id).ToArray(),
                    PermissionCodes = UserAccessResolver.ResolveEffectivePermissionCodes(activeRoles, catalog, denied),
                    ActorType = holder.ActorType.ToString(),
                    CorrelationId = correlationId,
                }
            );
        }
    }
}
