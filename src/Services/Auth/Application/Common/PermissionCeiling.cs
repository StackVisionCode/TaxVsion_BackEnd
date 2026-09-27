using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// El techo de delegación del tenant, escrito una sola vez (§27 del plan). Antes vivía repartido:
/// <see cref="RolePermissionGuard"/> lo aplicaba al crear y editar roles, y nadie lo aplicaba al
/// asignar roles, al invitar ni al aceptar una invitación.
///
/// <code>
/// Grantable(caller, tenant, targetActor) =
///       PermissionCatalog
///     ∩ { p | p.IsAssignableByTenant }
///     ∩ { p | targetActor ∈ p.AllowedActorTypes }
///     ∩ { p | tier(tenant) ≥ p.MinPlanTier }
///     ∩ { p | module(p) = null ∨ module(p) ∈ EnabledModules(tenant) }
///     − { p | p.PlatformOnly }
///     − { p | p.IsDangerous }
///     − { p | p.IsReserved }
/// </code>
///
/// <para>
/// <b>Dos mitades, a propósito.</b> <see cref="IsNeverGrantable"/> es la mitad <i>dura</i>: no
/// depende del plan contratado ni de nada que el tenant pueda comprar, así que se exige en TODOS
/// los caminos de concesión (crear rol, editar permisos, asignar rol, invitar, aceptar). La mitad
/// <i>comercial</i> (tier y módulo habilitado) se exige solo al escribir configuración nueva de un
/// rol: una configuración anterior a un downgrade queda <b>dormida</b>, no se borra, y el gate de
/// módulo en runtime es el que la vuelve inefectiva (§27, opción híbrida). Aplicarla también al
/// asignar dejaría un rol entero inasignable tras un downgrade, que es justo lo que el plan
/// descarta.
/// </para>
///
/// <para>
/// <b>El actor type no se valida acá.</b> Esa dimensión la cubre
/// <see cref="ActorTypeRoleGuard"/>, que devuelve el error específico
/// <c>Role.NotAssignableToActorType</c> con los códigos rechazados. Acá entra solo para calcular
/// <see cref="Grantable"/> (el flag que consume el picker de la UI), donde la fórmula tiene que
/// estar completa.
/// </para>
///
/// <para>
/// <b>∩ Effective(caller) no se aplica.</b> El plan lo marca como "solo relevante si roles.manage
/// llegara a delegarse". Exigirlo hoy rompería la gestión de roles del portal: el bundle del rol de
/// sistema Tenant Admin excluye los permisos <c>IsCustomerPortal</c>, así que un Tenant Admin no
/// tiene —ni debe tener— los permisos que concede a un rol de cliente. Queda documentado y sin
/// implementar hasta que roles.manage sea delegable de verdad.
/// </para>
///
/// Es una función pura: recibe el catálogo, el tier y los módulos ya resueltos, sin tocar
/// infraestructura.
/// </summary>
public static class PermissionCeiling
{
    /// <summary>Mismo código de error que ya devolvía <see cref="RolePermissionGuard"/>: el CRM
    /// desplegado lo lee tal cual, no se renombra.</summary>
    public const string ErrorCode = "Role.PermissionNotAssignable";

    /// <summary>
    /// Nunca concedible por un tenant, cualquiera sea su plan: reservado a la plataforma
    /// (<see cref="Permission.IsAssignableByTenant"/> = false o
    /// <see cref="Permission.PlatformOnly"/>), de riesgo alto y solo por asignación explícita del
    /// rol raíz (<see cref="Permission.IsDangerous"/>), o declarado sin nada que proteja todavía
    /// (<see cref="Permission.IsReserved"/>).
    /// <para>
    /// PlatformOnly e IsDangerous son hoy <b>redundantes</b> con IsAssignableByTenant en las ~190
    /// filas del catálogo (lo verifica PermissionCeilingFitnessTests): se leen explícito para que
    /// el techo no dependa de que las tres banderas sigan alineadas por convención.
    /// </para>
    /// </summary>
    public static bool IsNeverGrantable(Permission permission) =>
        !permission.IsAssignableByTenant || permission.PlatformOnly || permission.IsDangerous || permission.IsReserved;

    /// <summary>La mitad comercial: el plan contratado expone la permission y su módulo está
    /// habilitado. <paramref name="enabledModules"/> vacío = todavía no conocemos los módulos del
    /// tenant, no se bloquea (el gate en runtime es el enforcement real).</summary>
    public static bool IsWithinPlan(Permission permission, PlanTier planTier, IReadOnlySet<string> enabledModules)
    {
        if ((int)planTier < permission.MinPlanTier)
            return false;

        if (enabledModules.Count == 0)
            return true;

        var module = PermissionModuleMap.ModuleFor(permission.Code);
        return module is null || enabledModules.Contains(module);
    }

    /// <summary>La fórmula completa de §27, para el flag <c>grantable</c> del catálogo.</summary>
    public static bool IsGrantable(
        Permission permission,
        PlanTier planTier,
        IReadOnlySet<string> enabledModules,
        UserActorType? targetActorType
    ) =>
        !IsNeverGrantable(permission)
        && IsWithinPlan(permission, planTier, enabledModules)
        && (targetActorType is null || permission.AllowedActorTypes.Contains(targetActorType.Value));

    /// <summary>
    /// Valida el techo completo sobre los ids pedidos. Los ids que no existen en el catálogo se
    /// ignoran acá: los rechaza por separado la validación de existencia
    /// (<c>CreateRoleHandler.ValidatePermissionIdsAsync</c>), para no duplicar ese mensaje.
    /// </summary>
    public static Result Validate(
        IReadOnlyCollection<Permission> catalog,
        IReadOnlyCollection<Guid>? requestedPermissionIds,
        PlanTier planTier,
        IReadOnlySet<string> enabledModules
    ) =>
        Evaluate(
            catalog,
            requestedPermissionIds,
            permission => !IsNeverGrantable(permission) && IsWithinPlan(permission, planTier, enabledModules),
            "These permissions cannot be assigned by the tenant (reserved to the platform, "
                + "or not included in the current plan)"
        );

    /// <summary>
    /// Solo la mitad dura. Es la que corre al asignar un rol, al invitar con roles y al aceptar la
    /// invitación: ahí la configuración ya existía y puede estar dormida por un downgrade, así que
    /// medirla contra el plan la borraría en vez de dejarla inefectiva.
    /// </summary>
    public static Result ValidateNeverGrantable(
        IReadOnlyCollection<Permission> catalog,
        IReadOnlyCollection<Guid>? requestedPermissionIds
    ) =>
        Evaluate(
            catalog,
            requestedPermissionIds,
            permission => !IsNeverGrantable(permission),
            "These permissions are reserved to the platform and cannot be granted by a tenant"
        );

    /// <summary>
    /// Los permisos de estos roles que el tenant no podría conceder nunca. Los roles de SISTEMA se
    /// excluyen a propósito: los siembra la plataforma (el bundle raíz de Tenant Admin incluye
    /// permisos <see cref="Permission.IsDangerous"/> por diseño), así que medirlos contra el techo
    /// del tenant dejaría al rol "Tenant Admin" inasignable.
    /// </summary>
    public static Result ValidateRolesNeverGrantable(
        IReadOnlyCollection<Role> roles,
        IReadOnlyCollection<Permission> catalog
    )
    {
        var customPermissionIds = roles
            .Where(role => !role.IsSystem)
            .SelectMany(role => role.Permissions)
            .Select(link => link.PermissionId)
            .Distinct()
            .ToList();

        return ValidateNeverGrantable(catalog, customPermissionIds);
    }

    private static Result Evaluate(
        IReadOnlyCollection<Permission> catalog,
        IReadOnlyCollection<Guid>? requestedPermissionIds,
        Func<Permission, bool> isAllowed,
        string message
    )
    {
        if (requestedPermissionIds is null || requestedPermissionIds.Count == 0)
            return Result.Success();

        var byId = catalog.ToDictionary(permission => permission.Id);
        var rejected = new List<string>();

        foreach (var permissionId in requestedPermissionIds.Distinct())
        {
            if (byId.TryGetValue(permissionId, out var permission) && !isAllowed(permission))
                rejected.Add(permission.Code);
        }

        if (rejected.Count == 0)
            return Result.Success();

        return Result.Failure(new Error(ErrorCode, $"{message}: {string.Join(", ", rejected.OrderBy(code => code))}."));
    }
}
