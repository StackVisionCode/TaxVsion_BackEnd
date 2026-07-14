using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Guardarraíl anti-escalada de privilegios: valida que un conjunto de permisos pueda
/// incluirse en un rol CUSTOM del tenant (creado por su Tenant Admin), nunca en los
/// roles de sistema — esos se siembran vía <c>seeding: true</c> y no pasan por acá.
///
/// Regla de oro (Kubernetes RBAC / GitHub custom roles): un tenant nunca puede otorgar,
/// a través de un rol que crea, un permiso que (a) está reservado a la plataforma
/// (<see cref="Permission.IsAssignableByTenant"/> = false — billing, asientos, gestión
/// de roles) o (b) su plan contratado no expone todavía (<see cref="Permission.MinPlanTier"/>).
///
/// Es una función pura (sin acceso a datos) para poder testearla sin mocks de
/// infraestructura: recibe el catálogo y el tier ya resueltos.
/// </summary>
public static class RolePermissionGuard
{
    public static Result Validate(
        IReadOnlyCollection<Permission> catalog,
        IReadOnlyCollection<Guid>? requestedPermissionIds,
        PlanTier tenantPlanTier
    )
    {
        if (requestedPermissionIds is null || requestedPermissionIds.Count == 0)
            return Result.Success();

        var byId = catalog.ToDictionary(permission => permission.Id);
        var rejected = new List<string>();

        foreach (var permissionId in requestedPermissionIds.Distinct())
        {
            // Ids inexistentes en el catálogo los rechaza por separado la validación de
            // existencia (CreateRoleHandler.ValidatePermissionIdsAsync) — acá solo evaluamos
            // los que sí existen, para no duplicar ese mensaje de error.
            if (!byId.TryGetValue(permissionId, out var permission))
                continue;

            if (!permission.IsAssignableByTenant || (int)tenantPlanTier < permission.MinPlanTier)
                rejected.Add(permission.Code);
        }

        if (rejected.Count == 0)
            return Result.Success();

        return Result.Failure(
            new Error(
                "Role.PermissionNotAssignable",
                "These permissions cannot be assigned by the tenant (reserved to the platform, "
                    + $"or not included in the current plan): {string.Join(", ", rejected.OrderBy(code => code))}."
            )
        );
    }
}
