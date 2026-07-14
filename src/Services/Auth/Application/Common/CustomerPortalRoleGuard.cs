using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Fase A1 — garantiza que un usuario Tenant Customer (portal del cliente final) nunca
/// termine con un permiso que no esté explícitamente marcado para el portal
/// (<see cref="Permission.IsCustomerPortal"/>). Sin este guardarraíl, un Tenant Admin
/// podría (por error o abuso) asignarle a un cliente un rol con permisos internos
/// (ej. <c>users.manage</c>, <c>communication.settings.manage</c>) y ese cliente
/// terminaría con un JWT que lleva permisos que jamás debió tener — el mismo problema de
/// fondo que <see cref="RolePermissionGuard"/> resuelve para el eje plan/reservado, acá
/// aplicado al eje "portal vs interno".
///
/// Función pura: recibe los roles (con sus <see cref="Role.Permissions"/> ya cargados) y
/// el catálogo, sin tocar infraestructura — testeable sin mocks.
/// </summary>
public static class CustomerPortalRoleGuard
{
    public static Result ValidateRolesForCustomerPortal(
        IReadOnlyCollection<Role> roles,
        IReadOnlyCollection<Permission> catalog
    )
    {
        if (roles.Count == 0)
            return Result.Success();

        var portalFlagById = catalog.ToDictionary(
            permission => permission.Id,
            permission => permission.IsCustomerPortal
        );
        var codeById = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);

        var rejected = roles
            .SelectMany(role => role.Permissions)
            .Select(link => link.PermissionId)
            .Distinct()
            .Where(permissionId => portalFlagById.TryGetValue(permissionId, out var isPortal) && !isPortal)
            .Select(permissionId => codeById[permissionId])
            .ToList();

        if (rejected.Count == 0)
            return Result.Success();

        return Result.Failure(
            new Error(
                "Role.NotAssignableToCustomerPortal",
                "These roles include permissions not available to the customer portal: "
                    + string.Join(", ", rejected.OrderBy(code => code))
                    + "."
            )
        );
    }
}
