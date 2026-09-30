using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Roles.Commands;

public sealed record CreateRoleCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Name,
    string? Description,
    IReadOnlyList<Guid> PermissionIds,
    // RBAC Fase 3: opcional — el TenantAdmin declara para qué actor type es este rol custom
    // (staff vs. CustomerPortal). null se trata como "staff" (TenantEmployee/TenantAdmin) — ver
    // ActorTypeRoleGuard.ValidatePermissionsForActorType.
    UserActorType? TargetActorType = null,
    // RBAC hardening follow-up: actor type real del caller (desde el JWT, no del body) — el único
    // uso es rechazar TargetActorType=PlatformAdmin cuando quien llama no es PlatformAdmin. No
    // persiste en Role (se descarta tras la validación de abajo), así que hoy no era explotable,
    // pero faltaba el guardarraíl duro.
    bool CallerIsPlatformAdmin = false
);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes
)
{
    /// <summary>Actor types del tenant a los que este rol es asignable (todos sus permisos los
    /// permiten). Lo llena el listado (GET /auth/roles) para que el picker del frontend no ofrezca
    /// roles que el backend rechazaría con Role.NotAssignableToActorType. Vacío en respuestas de
    /// create/update (el frontend recarga el catálogo tras esas acciones).</summary>
    public IReadOnlyList<string> AssignableActorTypes { get; init; } = [];

    /// <summary>Actor type para el que se creó el rol (<see cref="Role.TargetActorType"/>), o null si
    /// no se declaró. La UI lo usa para saber que un rol es de clientes del portal y para no ofrecer
    /// permisos de staff al editarlo.</summary>
    public string? TargetActorType { get; init; }
}

public static class CreateRoleHandler
{
    public static async Task<Result<RoleResponse>> Handle(
        CreateRoleCommand command,
        IRoleRepository roles,
        ITenantPlanLimitsStore planLimits,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        // RBAC hardening follow-up: solo un PlatformAdmin puede declarar un rol destinado a
        // PlatformAdmin — cierra el hueco antes de cualquier acceso a datos.
        if (command.TargetActorType == UserActorType.PlatformAdmin && !command.CallerIsPlatformAdmin)
        {
            return Result.Failure<RoleResponse>(
                new Error(
                    "Role.TargetActorTypeForbidden",
                    "Only a PlatformAdmin can create a role targeted at the PlatformAdmin actor type."
                )
            );
        }

        if (await roles.NameExistsAsync(command.TenantId, command.Name?.Trim() ?? string.Empty, ct))
        {
            return Result.Failure<RoleResponse>(
                new Error("Role.NameConflict", "A role with this name already exists.")
            );
        }

        var roleResult = Role.Create(
            command.TenantId,
            command.Name!,
            command.Description,
            targetActorType: command.TargetActorType
        );
        if (roleResult.IsFailure)
            return Result.Failure<RoleResponse>(roleResult.Error);
        var role = roleResult.Value;

        // Los roles creados por acá SIEMPRE son custom (isSystem=false, ver Role.Create arriba)
        // — el guardarraíl anti-escalada aplica siempre. Los roles de sistema se siembran por
        // RoleRepository.EnsureSystemRolesAsync con seeding:true, sin pasar por este handler.
        var validation = await ValidatePermissionIdsAsync(
            roles,
            planLimits,
            command.TenantId,
            command.PermissionIds,
            ct
        );
        if (validation.IsFailure)
            return Result.Failure<RoleResponse>(validation.Error);

        // RBAC Fase 3: cierra el gap donde un rol custom podía persistirse mezclando permisos de
        // actor types incompatibles (ej. portal.folders.view + customers.view) y solo fallar
        // recién al intentar asignarlo — ver ActorTypeRoleGuard.ValidatePermissionsForActorType.
        var catalogForActorTypeCheck = await roles.GetPermissionsCatalogAsync(ct);
        var actorTypeCheck = ActorTypeRoleGuard.ValidatePermissionsForActorType(
            command.TargetActorType,
            command.PermissionIds ?? [],
            catalogForActorTypeCheck
        );
        if (actorTypeCheck.IsFailure)
            return Result.Failure<RoleResponse>(actorTypeCheck.Error);

        var setResult = role.SetPermissions(command.PermissionIds?.Distinct().ToList() ?? []);
        if (setResult.IsFailure)
            return Result.Failure<RoleResponse>(setResult.Error);

        await roles.AddAsync(role, ct);

        // También al crear: un consumidor que cachea rol → permisos no puede esperar al primer
        // set-permissions para enterarse de que el rol existe.
        await bus.PublishAsync(
            new RolePermissionsChangedIntegrationEvent
            {
                TenantId = command.TenantId,
                RoleId = role.Id,
                RoleName = role.Name,
                PermissionCodes = ResolvePermissionCodesFor(role, catalogForActorTypeCheck),
                PermissionsVersion = role.PermissionsVersion,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.CreatedByUserId,
                AuthAuditAction.RoleCreated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Role",
                targetId: role.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(await ToResponseAsync(role, roles, ct));
    }

    /// <summary>
    /// Valida (1) que todos los PermissionIds existan en el catálogo y (2) el techo de delegación
    /// (<see cref="PermissionCeiling"/>): que ninguno esté reservado a la plataforma ni exceda el
    /// plan contratado por el tenant. Se usa tanto al crear un rol como al reemplazar los permisos
    /// de uno existente — mismo contrato en los dos casos.
    /// </summary>
    /// <param name="alreadyGrantedPermissionIds">
    /// A4 (§27) — los permisos que el rol YA tenía. El techo se mide solo sobre el <b>delta
    /// añadido</b>: si no, un rol con permisos dormidos por un downgrade (el plan dejó de incluir su
    /// módulo) no se podía volver a guardar sin quitarlos primero, y la configuración anterior tiene
    /// que quedar dormida, no borrada. La validación de <i>existencia</i> sigue corriendo sobre el
    /// conjunto completo.
    /// </param>
    internal static async Task<Result> ValidatePermissionIdsAsync(
        IRoleRepository roles,
        ITenantPlanLimitsStore planLimits,
        Guid tenantId,
        IReadOnlyList<Guid>? permissionIds,
        CancellationToken ct,
        IReadOnlyCollection<Guid>? alreadyGrantedPermissionIds = null
    )
    {
        if (permissionIds is null || permissionIds.Count == 0)
            return Result.Success();

        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var known = catalog.Select(permission => permission.Id).ToHashSet();
        if (!permissionIds.All(known.Contains))
            return Result.Failure(new Error("Permission.NotFound", "One or more permissions do not exist."));

        var limits = await planLimits.GetAsync(tenantId, ct);
        var tier = PlanTierResolver.FromPlanCode(limits?.PlanCode);
        var modules = limits is null ? [] : JsonSerializer.Deserialize<List<string>>(limits.EnabledModulesJson) ?? [];
        var enabledModules = modules.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = AddedPermissionIds(permissionIds, alreadyGrantedPermissionIds);
        return PermissionCeiling.Validate(catalog, added, tier, enabledModules);
    }

    /// <summary>Los ids pedidos que el rol todavía no tenía.</summary>
    internal static IReadOnlyList<Guid> AddedPermissionIds(
        IReadOnlyCollection<Guid> requested,
        IReadOnlyCollection<Guid>? alreadyGranted
    )
    {
        if (alreadyGranted is null || alreadyGranted.Count == 0)
            return requested.Distinct().ToList();

        var current = alreadyGranted.ToHashSet();
        return requested.Distinct().Where(id => !current.Contains(id)).ToList();
    }

    /// <summary>Códigos de permiso del rol resueltos contra el catálogo, para el evento de integración.</summary>
    internal static string[] ResolvePermissionCodesFor(Role role, IReadOnlyList<Permission> catalog)
    {
        var codeByPermissionId = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        return role
            .Permissions.Select(link => link.PermissionId)
            .Where(codeByPermissionId.ContainsKey)
            .Select(id => codeByPermissionId[id])
            .ToArray();
    }

    internal static async Task<RoleResponse> ToResponseAsync(Role role, IRoleRepository roles, CancellationToken ct)
    {
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var codesById = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        return new RoleResponse(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem,
            role.IsActive,
            role.Permissions.Where(link => codesById.ContainsKey(link.PermissionId))
                .Select(link => codesById[link.PermissionId])
                .OrderBy(code => code)
                .ToList()
        )
        {
            TargetActorType = role.TargetActorType?.ToString(),
        };
    }
}

public sealed record UpdateRoleCommand(
    Guid TenantId,
    Guid RoleId,
    Guid UpdatedByUserId,
    string Name,
    string? Description
);

public static class UpdateRoleHandler
{
    public static async Task<Result> Handle(
        UpdateRoleCommand command,
        IRoleRepository roles,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var role = await roles.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.TenantId)
            return Result.Failure(new Error("Role.NotFound", "Role does not exist."));

        // A4 — la unicidad la garantizaba solo el índice único (TenantId, Name), así que renombrar
        // un rol al nombre de otro salía como un 409 genérico de infraestructura al guardar (o un
        // 500, según el traductor de excepciones). Acá devuelve el mismo Role.NameConflict que ya
        // devuelve la creación, y sin tocar la base. Se compara sin distinguir mayúsculas porque el
        // índice de SQL Server es case-insensitive por la collation por defecto.
        var newName = command.Name?.Trim() ?? string.Empty;
        if (
            !string.Equals(newName, role.Name, StringComparison.OrdinalIgnoreCase)
            && await roles.NameExistsAsync(command.TenantId, newName, ct)
        )
        {
            return Result.Failure(new Error("Role.NameConflict", "A role with this name already exists."));
        }

        var result = role.Update(command.Name, command.Description);
        if (result.IsFailure)
            return result;

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.UpdatedByUserId,
                AuthAuditAction.RoleUpdated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Role",
                targetId: role.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SetRolePermissionsCommand(
    Guid TenantId,
    Guid RoleId,
    Guid UpdatedByUserId,
    IReadOnlyList<Guid> PermissionIds
);

public static class SetRolePermissionsHandler
{
    public static async Task<Result> Handle(
        SetRolePermissionsCommand command,
        IRoleRepository roles,
        IUserRepository users,
        ITenantPlanLimitsStore planLimits,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var role = await roles.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.TenantId)
            return Result.Failure(new Error("Role.NotFound", "Role does not exist."));

        var currentPermissionIds = role.Permissions.Select(link => link.PermissionId).ToList();
        var requestedPermissionIds = command.PermissionIds?.Distinct().ToList() ?? [];
        var addedPermissionIds = CreateRoleHandler.AddedPermissionIds(requestedPermissionIds, currentPermissionIds);

        // Igual que en creación: SetPermissions (sin seeding:true) ya rechaza roles de sistema
        // por su cuenta (Role.System), así que este guardarraíl solo llega a aplicarse sobre
        // roles custom — pero lo evaluamos primero para devolver el error más específico.
        // A4: el techo se mide solo sobre el delta añadido (§27) para que un rol con permisos
        // dormidos por un downgrade siga siendo editable.
        var validation = await CreateRoleHandler.ValidatePermissionIdsAsync(
            roles,
            planLimits,
            command.TenantId,
            requestedPermissionIds,
            ct,
            currentPermissionIds
        );
        if (validation.IsFailure)
            return validation;

        var catalogForActorTypeCheck = await roles.GetPermissionsCatalogAsync(ct);

        // A4 (G7) — los titulares mandan: ninguno puede quedar con un permiso fuera de su actor
        // type por una edición del rol. Se valida el delta contra el actor type de cada titular
        // activo.
        var holders = await users.GetActiveByRoleAsync(command.TenantId, role.Id, ct);
        var holderActorTypes = holders.Select(holder => holder.ActorType).Distinct().ToList();
        if (holderActorTypes.Count > 0)
        {
            var holderCheck = ActorTypeRoleGuard.ValidatePermissionsForActorTypes(
                holderActorTypes,
                addedPermissionIds,
                catalogForActorTypeCheck
            );
            if (holderCheck.IsFailure)
                return holderCheck;
        }

        // A4 (G8) — y el destino declarado del rol: un rol de CustomerPortal se mide contra
        // CustomerPortal, no contra el staff. Antes se pasaba siempre null (= staff), así que un rol
        // de clientes quedaba inmutable: cualquier permiso de portal que ya tenía se rechazaba al
        // reguardarlo.
        //
        // Sin destino declarado (roles creados antes de la columna) hay dos casos: si tiene
        // titulares, el chequeo de arriba YA es la validación correcta —y la única aplicable, porque
        // medir contra "staff" rechazaría un rol de portal legado—; si no tiene ninguno, se mantiene
        // exactamente el comportamiento anterior (cada permiso válido para TenantEmployee o
        // TenantAdmin).
        if (role.TargetActorType is not null || holderActorTypes.Count == 0)
        {
            var actorTypeCheck = ActorTypeRoleGuard.ValidatePermissionsForActorType(
                role.TargetActorType,
                requestedPermissionIds,
                catalogForActorTypeCheck
            );
            if (actorTypeCheck.IsFailure)
                return actorTypeCheck;
        }

        var result = role.SetPermissions(requestedPermissionIds);
        if (result.IsFailure)
            return result;

        // Catálogo recargado acá (no reutiliza el de ValidatePermissionIdsAsync, que no lo
        // devuelve) para resolver los códigos de permiso efectivos del rol post-cambio — Fase 2
        // del plan de notificaciones dinámicas: sin este evento, un tenant con 50 empleados en
        // este rol nunca se entera de que perdieron/ganaron un permiso hasta que alguien les
        // toque el rol individualmente (que puede no pasar nunca).
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        await bus.PublishAsync(
            new RolePermissionsChangedIntegrationEvent
            {
                TenantId = command.TenantId,
                RoleId = role.Id,
                RoleName = role.Name,
                PermissionCodes = CreateRoleHandler.ResolvePermissionCodesFor(role, catalog),
                PermissionsVersion = role.PermissionsVersion,
                CorrelationId = correlation.CorrelationId,
            }
        );

        // Y por titular: el evento de arriba solo dice qué tiene el rol. Los denies por usuario
        // viven solo acá, así que los códigos efectivos de cada titular los tiene que resolver Auth.
        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            command.TenantId,
            role.Id,
            catalog,
            users,
            roles,
            bus,
            correlation.CorrelationId,
            ct
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.UpdatedByUserId,
                AuthAuditAction.RoleUpdated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Role",
                targetId: role.Id,
                detailsJson: """{"change":"permissions"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record DeactivateRoleCommand(Guid TenantId, Guid RoleId, Guid RequestedByUserId);

public static class DeactivateRoleHandler
{
    public static async Task<Result> Handle(
        DeactivateRoleCommand command,
        IRoleRepository roles,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var role = await roles.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.TenantId)
            return Result.Failure(new Error("Role.NotFound", "Role does not exist."));

        var result = role.Deactivate();
        if (result.IsFailure)
            return result;

        // Desactivar un rol le retira permisos a todos sus titulares. Sin este fan-out la
        // proyección de cada uno se quedaba con los permisos del rol desactivado.
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            command.TenantId,
            role.Id,
            catalog,
            users,
            roles,
            bus,
            correlation.CorrelationId,
            ct
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.RoleDeactivated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Role",
                targetId: role.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ReactivateRoleCommand(Guid TenantId, Guid RoleId, Guid RequestedByUserId);

/// <summary>
/// A4 — contraparte de <see cref="DeactivateRoleHandler"/>. Desactivar un rol nunca borró nada
/// (sus permisos y sus asignaciones siguen ahí), pero no había forma de deshacerlo por API: un rol
/// desactivado por error quedaba muerto y había que recrearlo a mano. Reactivar devuelve el acceso
/// a todos sus titulares, así que publica el mismo fan-out por titular que la desactivación.
/// </summary>
public static class ReactivateRoleHandler
{
    public static async Task<Result> Handle(
        ReactivateRoleCommand command,
        IRoleRepository roles,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var role = await roles.GetByIdAsync(command.RoleId, ct);
        if (role is null || role.TenantId != command.TenantId)
            return Result.Failure(new Error("Role.NotFound", "Role does not exist."));

        var result = role.Reactivate();
        if (result.IsFailure)
            return result;

        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        await RolePermissionsFanOut.PublishForRoleHoldersAsync(
            command.TenantId,
            role.Id,
            catalog,
            users,
            roles,
            bus,
            correlation.CorrelationId,
            ct
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.RoleReactivated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Role",
                targetId: role.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
