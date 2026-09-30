using System.Text.Json;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Roles.Commands;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
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
                    TargetActorType = role.TargetActorType?.ToString(),
                };
            })
            .ToList();

        return Result.Success(response);
    }
}

public sealed record PermissionResponse(Guid Id, string Code, string Module, string Description, bool IsCustomerPortal)
{
    /// <summary>
    /// A4.3 — banderas del techo (§27) para que el picker de la UI muestre por qué un permiso no se
    /// puede elegir en vez de descubrirlo con un 400 al guardar. Son aditivas: el CRM desplegado
    /// sigue leyendo los cinco campos posicionales de siempre.
    /// </summary>
    public bool IsAssignableByTenant { get; init; } = true;

    /// <summary>Exclusivo de operadores de la plataforma: ningún rol de tenant lo recibe.</summary>
    public bool PlatformOnly { get; init; }

    /// <summary>De riesgo alto (auto-escalada, financiero, legal, lock-out): solo por asignación
    /// explícita al rol raíz, nunca en un rol custom.</summary>
    public bool IsDangerous { get; init; }

    /// <summary>Declarado en el catálogo pero sin ningún endpoint que lo exija todavía: no se
    /// ofrece, para no vender una sensación de control que no existe.</summary>
    public bool IsReserved { get; init; }

    /// <summary>Tier mínimo de plan que lo expone (0 = Starter).</summary>
    public int MinPlanTier { get; init; }

    /// <summary>Módulo con el que lo mide el gate de entitlements en runtime
    /// (<see cref="PermissionModuleMap"/>), o null si no pertenece a ninguno. Distinto de
    /// <see cref="Module"/>, que es la agrupación de presentación del catálogo.</summary>
    public string? GateModule { get; init; }

    /// <summary>Actor types que pueden llegar a tenerlo a través de un rol.</summary>
    public IReadOnlyList<string> AllowedActorTypes { get; init; } = [];

    /// <summary>
    /// Si el tenant de quien consulta puede concederlo hoy en un rol custom: la fórmula completa de
    /// §27 (catálogo ∩ asignable ∩ tier ∩ módulo habilitado − PlatformOnly − peligrosas −
    /// reservadas). Sin tenant en el request se devuelve el techo sin la mitad comercial.
    /// </summary>
    public bool Grantable { get; init; }
}

/// <param name="TenantId">
/// Tenant de quien consulta, para resolver <see cref="PermissionResponse.Grantable"/> contra su plan
/// y sus módulos. null = sin contexto de tenant (el catálogo sigue siendo global).
/// </param>
public sealed record GetPermissionsCatalogQuery(Guid? TenantId = null);

public static class GetPermissionsCatalogHandler
{
    public static async Task<Result<IReadOnlyList<PermissionResponse>>> Handle(
        GetPermissionsCatalogQuery query,
        IRoleRepository roles,
        ITenantPlanLimitsStore planLimits,
        CancellationToken ct
    )
    {
        var catalog = await roles.GetPermissionsCatalogAsync(ct);

        var tier = PlanTier.Starter;
        IReadOnlySet<string> enabledModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (query.TenantId is { } tenantId)
        {
            var limits = await planLimits.GetAsync(tenantId, ct);
            tier = PlanTierResolver.FromPlanCode(limits?.PlanCode);
            var modules = limits is null
                ? []
                : JsonSerializer.Deserialize<List<string>>(limits.EnabledModulesJson) ?? [];
            enabledModules = modules.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        IReadOnlyList<PermissionResponse> response = catalog
            .Select(permission => new PermissionResponse(
                permission.Id,
                permission.Code,
                permission.Module,
                permission.Description,
                permission.IsCustomerPortal
            )
            {
                IsAssignableByTenant = permission.IsAssignableByTenant,
                PlatformOnly = permission.PlatformOnly,
                IsDangerous = permission.IsDangerous,
                IsReserved = permission.IsReserved,
                MinPlanTier = permission.MinPlanTier,
                GateModule = PermissionModuleMap.ModuleFor(permission.Code),
                AllowedActorTypes = permission.AllowedActorTypes.Select(actorType => actorType.ToString()).ToList(),
                Grantable = PermissionCeiling.IsGrantable(permission, tier, enabledModules, targetActorType: null),
            })
            .OrderBy(permission => permission.Module)
            .ThenBy(permission => permission.Code)
            .ToList();
        return Result.Success(response);
    }
}

/// <summary>Un titular activo del rol, lo mínimo para la pantalla "quién tiene este rol".</summary>
public sealed record RoleUserResponse(
    Guid Id,
    string Name,
    string LastName,
    string Email,
    string ActorType,
    bool IsActive
);

/// <summary>
/// A4.2 — quiénes tienen el rol. Sin esto la UI solo podía mostrar un contador
/// (<c>CountUsersInRoleAsync</c>), así que desactivar un rol era a ciegas: no había forma de ver a
/// quién le estabas quitando el acceso.
/// </summary>
public sealed record GetRoleUsersQuery(Guid TenantId, Guid RoleId);

public static class GetRoleUsersHandler
{
    public static async Task<Result<IReadOnlyList<RoleUserResponse>>> Handle(
        GetRoleUsersQuery query,
        IRoleRepository roles,
        IUserRepository users,
        CancellationToken ct
    )
    {
        var role = await roles.GetByIdAsync(query.RoleId, ct);
        if (role is null || role.TenantId != query.TenantId)
            return Result.Failure<IReadOnlyList<RoleUserResponse>>(new Error("Role.NotFound", "Role does not exist."));

        // Solo titulares activos: es la misma lista que recibe el fan-out de permisos, así que lo que
        // muestra la UI y a quién se le avisa del cambio no pueden divergir. Los dados de baja no son
        // "gente con este rol" para el que está decidiendo si lo desactiva.
        var holders = await users.GetActiveByRoleAsync(query.TenantId, role.Id, ct);

        IReadOnlyList<RoleUserResponse> response = holders
            .Select(holder => new RoleUserResponse(
                holder.Id,
                holder.Name,
                holder.LastName,
                holder.Email,
                holder.ActorType.ToString(),
                holder.IsActive
            ))
            .OrderBy(holder => holder.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result.Success(response);
    }
}
