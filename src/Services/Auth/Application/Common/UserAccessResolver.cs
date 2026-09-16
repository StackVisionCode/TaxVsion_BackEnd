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
        var activeCustomRoles = (await roles.GetUserRolesAsync(user.Id, ct)).Where(role => role.IsActive).ToList();

        var roleNames = new List<string>(user.Roles);
        roleNames.AddRange(activeCustomRoles.Select(role => role.Name));

        IReadOnlyList<string> permissions = await roles.GetEffectivePermissionCodesAsync(user.Id, ct);

        // Defaults are the pre-RBAC compatibility path (a user created before the role model, with no
        // role assignments yet). A user who HAS active roles but whose effective set is empty — e.g. the
        // deny layer restricted every permission their roles grant — must stay empty, or the denies would
        // be silently undone here.
        if (permissions.Count == 0 && activeCustomRoles.Count == 0)
            permissions = PermissionCatalog.DefaultsFor(user.ActorType).ToList();

        return (
            roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        );
    }

    /// <summary>
    /// Effective permission codes for a set of role aggregates (in-memory union): every distinct
    /// permission the roles grant, mapped to its code through the catalog. This is the single place the
    /// write-side commands (accept invitation, create tenant owner, assign roles) compute the codes they
    /// publish on <c>UserRolesChangedIntegrationEvent</c>, so the three call sites can never drift apart.
    /// The read-side path resolves the union from the database via
    /// <see cref="IRoleRepository.GetEffectivePermissionCodesAsync"/> instead.
    /// </summary>
    public static string[] ResolveEffectivePermissionCodes(
        IReadOnlyList<Role> roles,
        IReadOnlyList<Permission> catalog,
        IReadOnlyCollection<Guid>? deniedPermissionIds = null
    )
    {
        var denied = deniedPermissionIds is { Count: > 0 }
            ? deniedPermissionIds as ISet<Guid> ?? new HashSet<Guid>(deniedPermissionIds)
            : null;
        var codeByPermissionId = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        return roles
            .SelectMany(role => role.Permissions)
            .Select(rolePermission => rolePermission.PermissionId)
            .Distinct()
            .Where(permissionId => denied is null || !denied.Contains(permissionId))
            .Where(codeByPermissionId.ContainsKey)
            .Select(permissionId => codeByPermissionId[permissionId])
            .ToArray();
    }

    public static string EffectiveTimeZone(User user, Domain.Tenants.Tenant tenant) =>
        user.TimeZoneId ?? tenant.DefaultTimeZoneId;
}
