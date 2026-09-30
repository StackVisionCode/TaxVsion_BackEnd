using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Guardarraíl anti-escalada de privilegios al escribir la configuración de un rol CUSTOM del
/// tenant (creado por su Tenant Admin), nunca de los roles de sistema — esos se siembran vía
/// <c>seeding: true</c> y no pasan por acá.
///
/// <para>
/// Desde A4 esto es una fachada delgada sobre <see cref="PermissionCeiling"/>, el único techo del
/// servicio (§27 del plan). Se conserva el tipo —y su firma— porque es el nombre que usan los
/// handlers y los tests existentes; la regla vive en un solo lugar.
/// </para>
/// </summary>
public static class RolePermissionGuard
{
    public static Result Validate(
        IReadOnlyCollection<Permission> catalog,
        IReadOnlyCollection<Guid>? requestedPermissionIds,
        PlanTier tenantPlanTier,
        IReadOnlySet<string> enabledModules
    ) => PermissionCeiling.Validate(catalog, requestedPermissionIds, tenantPlanTier, enabledModules);
}
