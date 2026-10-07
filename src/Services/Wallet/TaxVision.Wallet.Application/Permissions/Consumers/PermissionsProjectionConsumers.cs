using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Permissions.Abstractions;
using TaxVision.Wallet.Domain.Permissions;

namespace TaxVision.Wallet.Application.Permissions.Consumers;

// ---------------------------------------------------------------------------
// RBAC Fase 7 — mantiene la proyección local de permisos que consulta
// ProjectionPermissionsSource (BuildingBlocks.Web) para enforzar perm_v sin llamar a Auth por
// HTTP en el hot path de autorización. Mismo patrón que CloudStorage/Notification/Signature:
// idempotente por PermissionsVersion, union-recompute en RolePermissionsChanged para no perder
// permisos de un usuario multi-rol.
// ---------------------------------------------------------------------------

public static class UserRolesChangedPermissionsProjectionConsumer
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
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
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

/// <summary>
/// Cachea rol → permisos. **No** recompone la unión de permisos de los usuarios del rol: Auth publica
/// <c>UserRolesChangedIntegrationEvent</c> por cada titular con sus códigos ya efectivos. La capa de
/// denies por usuario vive solo en Auth, así que recomponer la unión acá resucitaba un permiso
/// denegado en cuanto cambiaban los permisos de alguno de sus roles.
/// </summary>
public static class RolePermissionsChangedPermissionsProjectionConsumer
{
    public static async Task Handle(
        RolePermissionsChangedIntegrationEvent evt,
        IRolePermissionsProjectionRepository roleRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<RolePermissionsProjection> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
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
}
