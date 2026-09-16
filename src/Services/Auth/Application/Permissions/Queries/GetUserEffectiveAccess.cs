using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Permissions.Queries;

/// <summary>One role-granted permission the admin can toggle for a target user: the catalog id the
/// <c>PUT permission-overrides</c> call takes, its code/module/description for the label, and whether
/// it is currently denied (an OFF toggle).</summary>
public sealed record UserAccessPermission(
    Guid PermissionId,
    string Code,
    string Module,
    string Description,
    bool Denied
);

/// <summary>The target's role-granted permissions for one module, so the UI can render a module accordion.</summary>
public sealed record UserAccessModule(string Module, IReadOnlyList<UserAccessPermission> Permissions);

/// <summary>Everything the "Edit access" drawer needs in one call: the seat's actor type and active roles,
/// its role-granted permissions grouped by module (each flagged if currently denied), and the permission
/// version. Only role-granted permissions are returned — a deny on a permission no role grants is inert
/// and would render a meaningless toggle, so it is omitted (the replace-set write drops it anyway).</summary>
public sealed record UserEffectiveAccessResponse(
    Guid UserId,
    string ActorType,
    IReadOnlyList<string> Roles,
    IReadOnlyList<UserAccessModule> Modules,
    int PermissionsVersion
);

/// <summary>Admin-scoped read of another user's toggleable access (tenant-isolated). The self read
/// <c>GetMyEffectiveAccess</c> answers "why my 403" (plan module gating); this one answers "what can I
/// toggle for this user" (the deny editor), so the shapes differ deliberately.</summary>
public sealed record GetUserEffectiveAccessQuery(Guid TenantId, Guid TargetUserId);

public static class GetUserEffectiveAccessHandler
{
    public static async Task<Result<UserEffectiveAccessResponse>> Handle(
        GetUserEffectiveAccessQuery query,
        IUserRepository users,
        IRoleRepository roles,
        CancellationToken ct
    )
    {
        var target = await users.GetByIdAsync(query.TargetUserId, ct);
        if (target is null || target.TenantId != query.TenantId)
            return Result.Failure<UserEffectiveAccessResponse>(
                new Error("User.NotFound", "User does not exist in this tenant.")
            );

        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var permissionById = catalog.ToDictionary(permission => permission.Id);
        var activeRoles = (await roles.GetUserRolesAsync(target.Id, ct)).Where(role => role.IsActive).ToList();
        var deniedIds = (await roles.GetDeniedPermissionIdsAsync(target.Id, ct)).ToHashSet();

        var modules = activeRoles
            .SelectMany(role => role.Permissions)
            .Select(rolePermission => rolePermission.PermissionId)
            .Distinct()
            .Where(permissionById.ContainsKey)
            .Select(id => permissionById[id])
            .GroupBy(permission => permission.Module)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UserAccessModule(
                group.Key,
                group
                    .OrderBy(permission => permission.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(permission => new UserAccessPermission(
                        permission.Id,
                        permission.Code,
                        permission.Module,
                        permission.Description,
                        deniedIds.Contains(permission.Id)
                    ))
                    .ToList()
            ))
            .ToList();

        return Result.Success(
            new UserEffectiveAccessResponse(
                target.Id,
                target.ActorType.ToString(),
                activeRoles.Select(role => role.Name).ToArray(),
                modules,
                target.PermissionsVersion
            )
        );
    }
}
