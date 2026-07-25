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

    /// <summary>
    /// RBAC Fase 3 (RBAC_Hardening_Plan.md) — valida permisos SUELTOS (antes de que existan como
    /// <see cref="RolePermission"/> de un <see cref="Role"/> persistido) contra un actor type
    /// destino. Complementa a <see cref="ValidateRolesForActorType"/>, que valida roles YA
    /// existentes al asignarlos a un usuario — esta versión corre antes, al crear el rol o
    /// reemplazar su set de permisos, para que un rol mezclando permisos de actor types
    /// incompatibles (ej. <c>portal.folders.view</c> con <c>customers.view</c>) nunca llegue a
    /// persistirse, en vez de fallar recién al intentar asignarlo.
    /// </summary>
    /// <param name="targetActorType">
    /// Actor type declarado para el rol. <c>null</c> cuando no se conoce el destino (ej.
    /// <c>SetRolePermissionsHandler</c>, que edita un rol custom ya existente sin un
    /// <c>TargetActorType</c> propio) — en ese caso se exige que cada permiso sea válido para AL
    /// MENOS UNO de {TenantEmployee, TenantAdmin}, la defensa razonable para no dejar colar un
    /// permiso exclusivo de CustomerPortal en un rol sin destino declarado.
    /// </param>
    public static Result ValidatePermissionsForActorType(
        UserActorType? targetActorType,
        IReadOnlyCollection<Guid> permissionIds,
        IReadOnlyCollection<Permission> catalog
    )
    {
        var actorTypesToTry = targetActorType.HasValue
            ? [targetActorType.Value]
            : new[] { UserActorType.TenantEmployee, UserActorType.TenantAdmin };

        var byId = catalog.ToDictionary(permission => permission.Id);
        var rejected = new List<string>();

        foreach (var permissionId in permissionIds.Distinct())
        {
            if (!byId.TryGetValue(permissionId, out var permission))
                continue;

            if (!actorTypesToTry.Any(actorType => permission.AllowedActorTypes.Contains(actorType)))
                rejected.Add(permission.Code);
        }

        if (rejected.Count == 0)
            return Result.Success();

        return Result.Failure(
            new Error(
                "Role.NotAssignableToActorType",
                "The following permissions are not assignable to actor type "
                    + (targetActorType?.ToString() ?? "staff (TenantEmployee/TenantAdmin)")
                    + ": "
                    + string.Join(", ", rejected.OrderBy(code => code))
            )
        );
    }
}
