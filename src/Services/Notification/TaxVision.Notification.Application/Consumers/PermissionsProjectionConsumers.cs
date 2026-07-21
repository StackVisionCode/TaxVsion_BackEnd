using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Application.Consumers;

// ---------------------------------------------------------------------------
// Fase 4 del plan de notificaciones dinámicas: mantiene la proyección local de
// permisos que usa IRecipientResolver para resolver audiencias ByPermission
// (ver CloudStorageEventConsumers.cs, los 2 consumers que dejan de usar el
// placeholder "role:TenantAdmin"). Mismo patrón que Signature's
// UserRolesChangedConsumer (idempotente por PermissionsVersion) + el
// union-recompute de RolePermissionsChanged ya implementado en Communication
// (Fase 2, auth-consumers.ts) para no perder permisos de un usuario multi-rol.
// ---------------------------------------------------------------------------

public static class UserRolesChangedConsumer
{
    public static async Task Handle(
        UserRolesChangedIntegrationEvent evt,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<UserPermissionsProjection> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var existing = await repository.GetAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
            {
                var projection = UserPermissionsProjection.Create(
                    evt.TenantId,
                    evt.UserId,
                    evt.PermissionsVersion,
                    evt.PermissionCodes,
                    evt.RoleIds
                );
                await repository.AddAsync(projection, ct);
                logger.LogInformation(
                    "UserPermissionsProjection created for {UserId} version {Version}.",
                    evt.UserId,
                    evt.PermissionsVersion
                );
            }
            else
            {
                existing.ApplyIfNewer(evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds);
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

public static class RolePermissionsChangedConsumer
{
    public static async Task Handle(
        RolePermissionsChangedIntegrationEvent evt,
        IRolePermissionsProjectionRepository roleRepository,
        IUserPermissionsProjectionRepository userRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<RolePermissionsProjection> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var roleProjection = await UpsertRoleProjectionAsync(evt, roleRepository, ct);

            var affectedUsers = await userRepository.FindActiveByTenantAndRoleIdAsync(evt.TenantId, evt.RoleId, ct);
            if (affectedUsers.Count == 0)
            {
                await unitOfWork.SaveChangesAsync(ct);
                return;
            }

            await ReapplyPermissionsUnionAsync(evt.TenantId, roleProjection, affectedUsers, roleRepository, ct);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "RolePermissionsChanged: recomputed union for {Count} affected user(s) of role {RoleId}.",
                affectedUsers.Count,
                evt.RoleId
            );
        }
    }

    private static async Task<RolePermissionsProjection> UpsertRoleProjectionAsync(
        RolePermissionsChangedIntegrationEvent evt,
        IRolePermissionsProjectionRepository roleRepository,
        CancellationToken ct
    )
    {
        var existing = await roleRepository.GetAsync(evt.TenantId, evt.RoleId, ct);
        if (existing is null)
        {
            var created = RolePermissionsProjection.Create(
                evt.TenantId,
                evt.RoleId,
                evt.RoleName,
                evt.PermissionsVersion,
                evt.PermissionCodes
            );
            await roleRepository.AddAsync(created, ct);
            return created;
        }

        existing.ApplyIfNewer(evt.RoleName, evt.PermissionsVersion, evt.PermissionCodes);
        return existing;
    }

    /// <summary>
    /// Un usuario con VARIOS roles no puede sobrescribirse solo con los códigos del rol que
    /// cambió, o perdería los permisos heredados de sus otros roles — recompone la unión
    /// completa contra el cache de RolePermissionsProjection. <paramref name="changedRole"/>
    /// se inyecta directamente (en vez de volver a consultarlo) porque su UPDATE todavía no
    /// se guardó en esta transacción.
    /// </summary>
    private static async Task ReapplyPermissionsUnionAsync(
        Guid tenantId,
        RolePermissionsProjection changedRole,
        IReadOnlyList<UserPermissionsProjection> affectedUsers,
        IRolePermissionsProjectionRepository roleRepository,
        CancellationToken ct
    )
    {
        var otherRoleIds = affectedUsers
            .SelectMany(user => user.RoleIds())
            .Where(roleId => roleId != changedRole.Id)
            .Distinct()
            .ToList();
        var otherRoles =
            otherRoleIds.Count == 0 ? [] : await roleRepository.FindByRoleIdsAsync(tenantId, otherRoleIds, ct);

        var rolesById = otherRoles.ToDictionary(role => role.Id, role => role);
        rolesById[changedRole.Id] = changedRole;

        foreach (var user in affectedUsers)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleId in user.RoleIds())
            {
                if (rolesById.TryGetValue(roleId, out var role))
                {
                    foreach (var code in role.PermissionCodes())
                        union.Add(code);
                }
            }
            user.ReapplyPermissionsUnion(union);
        }
    }
}

/// <summary>Mantiene IsActive correcto en la proyección — sin esto, ByPermission seguiría notificando a un usuario desactivado.</summary>
public static class UserDeactivatedPermissionsProjectionConsumer
{
    public static async Task Handle(
        UserDeactivatedIntegrationEvent evt,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var existing = await repository.GetAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
                return;

            existing.MarkInactive();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
