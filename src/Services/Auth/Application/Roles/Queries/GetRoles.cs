using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Roles.Commands;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Roles.Queries;

public sealed record GetRolesQuery(Guid TenantId);

public static class GetRolesHandler
{
    // Actor types del tenant asignables por rol (PlatformAdmin queda fuera: es god-mode, no se asigna
    // por rol). Un rol es asignable a X si TODOS sus permisos permiten X (misma regla que ActorTypeRoleGuard).
    private static readonly UserActorType[] TenantActorTypes =
    [
        UserActorType.TenantEmployee,
        UserActorType.TenantAdmin,
        UserActorType.CustomerPortal,
    ];

    public static async Task<Result<IReadOnlyList<RoleResponse>>> Handle(
        GetRolesQuery query,
        IRoleRepository roles,
        CancellationToken ct
    )
    {
        var tenantRoles = await roles.GetByTenantAsync(query.TenantId, ct);
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var permissionsById = catalog.ToDictionary(permission => permission.Id);

        IReadOnlyList<RoleResponse> response = tenantRoles
            .Select(role =>
            {
                var rolePermissions = role
                    .Permissions.Where(link => permissionsById.ContainsKey(link.PermissionId))
                    .Select(link => permissionsById[link.PermissionId])
                    .ToList();

                var assignableActorTypes = TenantActorTypes
                    .Where(actorType => rolePermissions.All(p => p.AllowedActorTypes.Contains(actorType)))
                    .Select(actorType => actorType.ToString())
                    .ToList();

                return new RoleResponse(
                    role.Id,
                    role.Name,
                    role.Description,
                    role.IsSystem,
                    role.IsActive,
                    rolePermissions.Select(p => p.Code).OrderBy(code => code).ToList()
                )
                {
                    AssignableActorTypes = assignableActorTypes,
                };
            })
            .ToList();

        return Result.Success(response);
    }
}

public sealed record PermissionResponse(Guid Id, string Code, string Module, string Description, bool IsCustomerPortal);

public sealed record GetPermissionsCatalogQuery;

public static class GetPermissionsCatalogHandler
{
    public static async Task<Result<IReadOnlyList<PermissionResponse>>> Handle(
        GetPermissionsCatalogQuery query,
        IRoleRepository roles,
        CancellationToken ct
    )
    {
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        IReadOnlyList<PermissionResponse> response = catalog
            .Select(permission => new PermissionResponse(
                permission.Id,
                permission.Code,
                permission.Module,
                permission.Description,
                permission.IsCustomerPortal
            ))
            .OrderBy(permission => permission.Module)
            .ThenBy(permission => permission.Code)
            .ToList();
        return Result.Success(response);
    }
}
