using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Resuelve los roles efectivos (rol de actor + roles personalizados) y los permisos
/// efectivos de un usuario. Si el usuario aún no tiene roles RBAC asignados
/// (creado antes del modelo), aplica los permisos por defecto de su ActorType.
/// </summary>
public static class UserAccessResolver
{
    public static async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> ResolveAsync(
        User user,
        IRoleRepository roles,
        CancellationToken ct = default
    )
    {
        var customRoles = await roles.GetUserRolesAsync(user.Id, ct);

        var roleNames = new List<string>(user.Roles);
        roleNames.AddRange(customRoles.Where(role => role.IsActive).Select(role => role.Name));

        IReadOnlyList<string> permissions = await roles.GetEffectivePermissionCodesAsync(user.Id, ct);

        if (permissions.Count == 0)
            permissions = PermissionCatalog.DefaultsFor(user.ActorType).ToList();

        return (
            roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        );
    }

    public static string EffectiveTimeZone(User user, Domain.Tenants.Tenant tenant) =>
        user.TimeZoneId ?? tenant.DefaultTimeZoneId;
}
