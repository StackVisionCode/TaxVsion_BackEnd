using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Roles.Commands;

namespace TaxVision.Auth.Application.Roles.Queries;

public sealed record GetRolesQuery(Guid TenantId);

public static class GetRolesHandler
{
    public static async Task<Result<IReadOnlyList<RoleResponse>>> Handle(
        GetRolesQuery query,
        IRoleRepository roles,
        CancellationToken ct)
    {
        var tenantRoles = await roles.GetByTenantAsync(query.TenantId, ct);
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var codesById = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);

        IReadOnlyList<RoleResponse> response = tenantRoles
            .Select(role => new RoleResponse(
                role.Id,
                role.Name,
                role.Description,
                role.IsSystem,
                role.IsActive,
                role.Permissions
                    .Where(link => codesById.ContainsKey(link.PermissionId))
                    .Select(link => codesById[link.PermissionId])
                    .OrderBy(code => code)
                    .ToList()))
            .ToList();

        return Result.Success(response);
    }
}

public sealed record PermissionResponse(
    Guid Id,
    string Code,
    string Module,
    string Description,
    bool IsCustomerPortal);

public sealed record GetPermissionsCatalogQuery;

public static class GetPermissionsCatalogHandler
{
    public static async Task<Result<IReadOnlyList<PermissionResponse>>> Handle(
        GetPermissionsCatalogQuery query,
        IRoleRepository roles,
        CancellationToken ct)
    {
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        IReadOnlyList<PermissionResponse> response = catalog
            .Select(permission => new PermissionResponse(
                permission.Id,
                permission.Code,
                permission.Module,
                permission.Description,
                permission.IsCustomerPortal))
            .OrderBy(permission => permission.Module)
            .ThenBy(permission => permission.Code)
            .ToList();
        return Result.Success(response);
    }
}
