using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

// ---------------------------------------------------------------------------
// Desactivar usuario (libera asiento, corta sesiones)
// ---------------------------------------------------------------------------

/// <summary>Solicitud para desactivar un usuario: libera su asiento del plan y corta sus sesiones activas.</summary>
public sealed record DeactivateUserCommand(Guid TenantId, Guid TargetUserId, Guid RequestedByUserId);

/// <summary>Desactiva al usuario objetivo, revoca sesiones y tokens, y publica el evento de integración correspondiente.</summary>
public static class DeactivateUserHandler
{
    public static async Task<Result> Handle(
        DeactivateUserCommand command,
        IUserRepository users,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (command.TargetUserId == command.RequestedByUserId)
            return Result.Failure(new Error("User.SelfAction", "You cannot deactivate your own account."));

        var target = await users.GetByIdAsync(command.TargetUserId, ct);
        if (target is null || target.TenantId != command.TenantId)
            return Result.Failure(new Error("User.NotFound", "User does not exist in this tenant."));

        if (!target.IsActive)
            return Result.Success();

        target.Deactivate(DateTime.UtcNow);

        var active = await sessions.GetActiveSessionsByUserAsync(target.Id, ct);
        foreach (var session in active)
            await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        await sessions.RevokeAllForUserAsync(target.Id, "admin_revoke", null, ct);

        await bus.PublishAsync(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = target.TenantId,
                UserId = target.Id,
                Email = target.Email,
                ActorType = target.ActorType.ToString(),
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.UserDeactivated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: target.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Reactivar usuario (consume asiento: valida límites del plan)
// ---------------------------------------------------------------------------

/// <summary>Solicitud para reactivar un usuario previamente desactivado (vuelve a consumir un asiento del plan).</summary>
public sealed record ReactivateUserCommand(Guid TenantId, Guid TargetUserId, Guid RequestedByUserId);

/// <summary>Reactiva al usuario tras validar que el plan del tenant tiene asientos disponibles.</summary>
public static class ReactivateUserHandler
{
    public static async Task<Result> Handle(
        ReactivateUserCommand command,
        IUserRepository users,
        IInvitationRepository invitations,
        ITenantPlanLimitsStore planLimits,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var target = await users.GetByIdAsync(command.TargetUserId, ct);
        if (target is null || target.TenantId != command.TenantId)
            return Result.Failure(new Error("User.NotFound", "User does not exist in this tenant."));

        if (target.IsActive)
            return Result.Success();

        var seatResult = await PlanGuard.EnsureSeatAvailableAsync(command.TenantId, planLimits, users, invitations, ct);
        if (seatResult.IsFailure)
            return seatResult;

        target.Reactivate();

        await bus.PublishAsync(
            new UserReactivatedIntegrationEvent
            {
                TenantId = target.TenantId,
                UserId = target.Id,
                Email = target.Email,
                ActorType = target.ActorType.ToString(),
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.UserReactivated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: target.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Perfil propio (nombre, apellido, zona horaria)
// ---------------------------------------------------------------------------

/// <summary>Solicitud del propio usuario para actualizar su perfil: nombre, apellido y zona horaria.</summary>
public sealed record UpdateMyProfileCommand(Guid UserId, string Name, string LastName, string? TimeZoneId);

/// <summary>Aplica los cambios de perfil y zona horaria del usuario y deja constancia en la auditoría.</summary>
public static class UpdateMyProfileHandler
{
    public static async Task<Result> Handle(
        UpdateMyProfileCommand command,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(new Error("User.NotFound", "User does not exist."));

        var nameChanged = user.Name != command.Name || user.LastName != command.LastName;

        var profileResult = user.UpdateProfile(command.Name, command.LastName);
        if (profileResult.IsFailure)
            return profileResult;

        var timeZoneResult = user.SetTimeZone(command.TimeZoneId);
        if (timeZoneResult.IsFailure)
            return timeZoneResult;

        // Solo publicar cuando el nombre realmente cambio — evita ruido en el bus
        // (y en la proyeccion de displayName de Communication) cuando el usuario
        // solo actualizo su TimeZoneId.
        if (nameChanged)
        {
            await bus.PublishAsync(
                new UserProfileUpdatedIntegrationEvent
                {
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    Name = user.Name,
                    LastName = user.LastName,
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.UserProfileUpdated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Asignación de roles (reemplaza el conjunto; invalida permisos de JWT viejos)
// ---------------------------------------------------------------------------

/// <summary>Solicitud para reemplazar el conjunto de roles de un usuario (invalida los permisos de los JWT anteriores).</summary>
public sealed record AssignUserRolesCommand(
    Guid TenantId,
    Guid TargetUserId,
    IReadOnlyList<Guid> RoleIds,
    Guid AssignedByUserId
);

/// <summary>Valida y reemplaza los roles del usuario, incrementa su versión de permisos y publica el evento de cambio.</summary>
public static class AssignUserRolesHandler
{
    public static async Task<Result> Handle(
        AssignUserRolesCommand command,
        IUserRepository users,
        IRoleRepository roles,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var target = await users.GetByIdAsync(command.TargetUserId, ct);
        if (target is null || target.TenantId != command.TenantId)
            return Result.Failure(new Error("User.NotFound", "User does not exist in this tenant."));

        var requestedIds = command.RoleIds?.Distinct().ToList() ?? [];
        var tenantRoles = await roles.GetByIdsAsync(command.TenantId, requestedIds, ct);
        if (tenantRoles.Count != requestedIds.Count)
            return Result.Failure(new Error("Role.NotFound", "One or more roles do not exist in this tenant."));

        if (tenantRoles.Any(role => !role.IsActive))
            return Result.Failure(new Error("Role.Inactive", "One or more roles are inactive."));

        // Catálogo cargado siempre: lo necesita el guard de actor type
        // y, ahora, el cálculo de PermissionCodes del evento publicado más abajo.
        var catalog = await roles.GetPermissionsCatalogAsync(ct);

        // Fase A1 + Fase 2 (Actor_Type_Authorization_Layers_Plan.md): ningún usuario debe terminar
        // con un permiso fuera de su actor type colado por un rol mal asignado, en cualquier
        // sentido (ver ActorTypeRoleGuard).
        var actorTypeGuard = ActorTypeRoleGuard.ValidateRolesForActorType(target.ActorType, tenantRoles, catalog);
        if (actorTypeGuard.IsFailure)
            return actorTypeGuard;

        await roles.ReplaceUserRolesAsync(target.Id, requestedIds, command.AssignedByUserId, ct);
        target.BumpPermissionsVersion();

        await bus.PublishAsync(
            new UserRolesChangedIntegrationEvent
            {
                TenantId = target.TenantId,
                UserId = target.Id,
                PermissionsVersion = target.PermissionsVersion,
                RoleNames = tenantRoles.Select(role => role.Name).ToArray(),
                RoleIds = tenantRoles.Select(role => role.Id).ToArray(),
                PermissionCodes = ResolveEffectivePermissionCodes(tenantRoles, catalog),
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.AssignedByUserId,
                AuthAuditAction.UserRolesChanged,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: target.Id,
                detailsJson: System.Text.Json.JsonSerializer.Serialize(
                    new { roles = tenantRoles.Select(role => role.Name) }
                )
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Códigos de permiso efectivos del set de roles NUEVO, calculados en memoria a partir
    /// de <paramref name="tenantRoles"/> (ya resuelto por el handler) y el catálogo. No usa
    /// <see cref="IRoleRepository.GetEffectivePermissionCodesAsync"/> porque esa consulta
    /// golpea la base directamente y en este punto todavía no se llamó SaveChangesAsync —
    /// devolvería los permisos VIEJOS, no los que se están por persistir.
    /// </summary>
    private static string[] ResolveEffectivePermissionCodes(
        IReadOnlyList<Role> tenantRoles,
        IReadOnlyList<Permission> catalog
    )
    {
        var codeByPermissionId = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        return tenantRoles
            .SelectMany(role => role.Permissions)
            .Select(rolePermission => rolePermission.PermissionId)
            .Distinct()
            .Where(codeByPermissionId.ContainsKey)
            .Select(permissionId => codeByPermissionId[permissionId])
            .ToArray();
    }
}
