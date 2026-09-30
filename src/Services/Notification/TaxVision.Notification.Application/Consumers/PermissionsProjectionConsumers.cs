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
        INotificationRecipientPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<NotificationRecipientPermissionsProjection> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var existing = await repository.GetAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
            {
                var projection = NotificationRecipientPermissionsProjection.Create(
                    evt.TenantId,
                    evt.UserId,
                    evt.PermissionsVersion,
                    evt.PermissionCodes,
                    evt.RoleIds
                );
                await repository.AddAsync(projection, ct);
                logger.LogInformation(
                    "NotificationRecipientPermissionsProjection created for {UserId} version {Version}.",
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

/// <summary>
/// Cachea rol → permisos. **No** recompone la unión de permisos de los usuarios del rol: Auth publica
/// <c>UserRolesChangedIntegrationEvent</c> por cada titular con sus códigos ya efectivos. La capa de
/// denies por usuario vive solo en Auth, así que recomponer la unión acá resucitaba un permiso
/// denegado en cuanto cambiaban los permisos de alguno de sus roles.
/// </summary>
public static class RolePermissionsChangedConsumer
{
    public static async Task Handle(
        RolePermissionsChangedIntegrationEvent evt,
        INotificationRecipientRolePermissionsProjectionRepository roleRepository,
        INotificationRecipientPermissionsProjectionRepository userRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<NotificationRecipientRolePermissionsProjection> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await UpsertRoleProjectionAsync(evt, roleRepository, ct);

            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "RolePermissionsChanged: role {RoleId} cached at version {Version}.",
                evt.RoleId,
                evt.PermissionsVersion
            );
        }
    }

    private static async Task<NotificationRecipientRolePermissionsProjection> UpsertRoleProjectionAsync(
        RolePermissionsChangedIntegrationEvent evt,
        INotificationRecipientRolePermissionsProjectionRepository roleRepository,
        CancellationToken ct
    )
    {
        var existing = await roleRepository.GetAsync(evt.TenantId, evt.RoleId, ct);
        if (existing is null)
        {
            var created = NotificationRecipientRolePermissionsProjection.Create(
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
}

/// <summary>Mantiene IsActive correcto en la proyección — sin esto, ByPermission seguiría notificando a un usuario desactivado.</summary>
public static class UserDeactivatedPermissionsProjectionConsumer
{
    public static async Task Handle(
        UserDeactivatedIntegrationEvent evt,
        INotificationRecipientPermissionsProjectionRepository repository,
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
