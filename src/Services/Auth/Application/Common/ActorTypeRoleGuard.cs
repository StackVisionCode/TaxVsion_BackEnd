using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Fase 2 de Actor_Type_Authorization_Layers_Plan.md — reemplaza a <c>CustomerPortalRoleGuard</c>
/// (que solo validaba un sentido: Tenant Customer nunca con un permiso interno colado) con la
/// versión simétrica: para CUALQUIER <see cref="UserActorType"/> al que se le vayan a asignar
/// roles, ninguno de los permisos que esos roles otorgan puede quedar fuera de
/// <see cref="Permission.AllowedActorTypes"/> — un permiso "solo customer" no se puede colar en
/// un rol de empleado, y uno "solo staff" no se puede colar en un rol de customer. Con la
/// inferencia por defecto de <see cref="Permission.InferAllowedActorTypes"/>, hoy esto es un
/// no-op para roles de staff (los 3 actor types no-portal quedan incluidos por default) — se
/// vuelve una restricción real permiso por permiso a medida que la Fase 7 los anota explícito.
///
/// Función pura: recibe los roles (con sus <see cref="Role.Permissions"/> ya cargados) y el
/// catálogo, sin tocar infraestructura — testeable sin mocks.
/// </summary>
public static class ActorTypeRoleGuard
{
    public static Result ValidateRolesForActorType(
        UserActorType actorType,
        IReadOnlyCollection<Role> roles,
        IReadOnlyCollection<Permission> catalog
    )
    {
        if (roles.Count == 0)
            return Result.Success();

        var allowedActorTypesById = catalog.ToDictionary(
            permission => permission.Id,
            permission => permission.AllowedActorTypes
        );
        var codeById = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);

        var rejected = roles
            .SelectMany(role => role.Permissions)
            .Select(link => link.PermissionId)
            .Distinct()
            .Where(permissionId =>
                allowedActorTypesById.TryGetValue(permissionId, out var allowed) && !allowed.Contains(actorType)
            )
            .Select(permissionId => codeById[permissionId])
            .ToList();

        if (rejected.Count == 0)
            return Result.Success();

        return Result.Failure(
            new Error(
                "Role.NotAssignableToActorType",
                $"These roles include permissions not available to {actorType}: "
                    + string.Join(", ", rejected.OrderBy(code => code))
                    + "."
            )
        );
    }
}
