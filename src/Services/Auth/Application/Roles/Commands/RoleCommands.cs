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
    UserActorType? TargetActorType = null
);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes
);

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
        CancellationToken ct
    )
    {
        if (await roles.NameExistsAsync(command.TenantId, command.Name?.Trim() ?? string.Empty, ct))
        {
            return Result.Failure<RoleResponse>(
                new Error("Role.NameConflict", "A role with this name already exists.")
            );
        }

        var roleResult = Role.Create(command.TenantId, command.Name!, command.Description);
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
    /// Valida (1) que todos los PermissionIds existan en el catálogo y (2) el guardarraíl
    /// anti-escalada (<see cref="RolePermissionGuard"/>): que ninguno esté reservado a la
    /// plataforma ni exceda el plan contratado por el tenant. Se usa tanto al crear un rol
    /// como al reemplazar los permisos de uno existente — mismo contrato en los dos casos.
    /// </summary>
    internal static async Task<Result> ValidatePermissionIdsAsync(
        IRoleRepository roles,
        ITenantPlanLimitsStore planLimits,
        Guid tenantId,
        IReadOnlyList<Guid>? permissionIds,
        CancellationToken ct
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
        return RolePermissionGuard.Validate(catalog, permissionIds, tier);
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
        );
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

        // Igual que en creación: SetPermissions (sin seeding:true) ya rechaza roles de sistema
        // por su cuenta (Role.System), así que este guardarraíl solo llega a aplicarse sobre
        // roles custom — pero lo evaluamos primero para devolver el error más específico.
        var validation = await CreateRoleHandler.ValidatePermissionIdsAsync(
            roles,
            planLimits,
            command.TenantId,
            command.PermissionIds,
            ct
        );
        if (validation.IsFailure)
            return validation;

        // RBAC Fase 3: mismo guardarraíl que CreateRoleHandler — acá el rol ya existe y no
        // conocemos su actor type destino, así que se valida contra "staff" (null → ver
        // ActorTypeRoleGuard.ValidatePermissionsForActorType), la defensa razonable para no
        // dejar colar un permiso exclusivo de CustomerPortal en un rol custom sin destino.
        var catalogForActorTypeCheck = await roles.GetPermissionsCatalogAsync(ct);
        var actorTypeCheck = ActorTypeRoleGuard.ValidatePermissionsForActorType(
            null,
            command.PermissionIds ?? [],
            catalogForActorTypeCheck
        );
        if (actorTypeCheck.IsFailure)
            return actorTypeCheck;

        var result = role.SetPermissions(command.PermissionIds?.Distinct().ToList() ?? []);
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
                PermissionCodes = ResolvePermissionCodes(role, catalog),
                PermissionsVersion = role.PermissionsVersion,
                CorrelationId = correlation.CorrelationId,
            }
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

    private static string[] ResolvePermissionCodes(Role role, IReadOnlyList<Permission> catalog)
    {
        var codeByPermissionId = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        return role
            .Permissions.Select(link => link.PermissionId)
            .Where(codeByPermissionId.ContainsKey)
            .Select(id => codeByPermissionId[id])
            .ToArray();
    }
}

public sealed record DeactivateRoleCommand(Guid TenantId, Guid RoleId, Guid RequestedByUserId);

public static class DeactivateRoleHandler
{
    public static async Task<Result> Handle(
        DeactivateRoleCommand command,
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

        var result = role.Deactivate();
        if (result.IsFailure)
            return result;

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
