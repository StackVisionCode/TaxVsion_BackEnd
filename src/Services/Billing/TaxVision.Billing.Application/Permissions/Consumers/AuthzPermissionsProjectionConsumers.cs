using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Permissions;

namespace TaxVision.Billing.Application.Permissions.Consumers;

// H-01 / RBAC Fase 7 — mantiene la proyección local de permisos de AUTORIZACIÓN que consulta
// ProjectionPermissionsSource (BuildingBlocks.Web) para enforzar perm_v sin llamar a Auth por HTTP en
// el hot path. Idempotente por PermissionsVersion; union-recompute en RolePermissionsChanged para no
// perder permisos de un usuario multi-rol. Los eventos llegan por la cola billing-events (bindeada al
// exchange taxvision-events donde Auth publica). Mismo patrón que Documents/Signature/CloudStorage.

public static class AuthzUserRolesChangedPermissionsProjectionConsumer
{
    public static async Task Handle(
        UserRolesChangedIntegrationEvent evt,
        IAuthzUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<AuthzUserPermissionsProjection> logger,
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
                var projection = AuthzUserPermissionsProjection.Create(
                    evt.TenantId,
                    evt.UserId,
                    evt.PermissionsVersion,
                    evt.PermissionCodes,
                    evt.RoleIds
                );
                await repository.AddAsync(projection, ct);
                logger.LogInformation(
                    "AuthzUserPermissionsProjection created for {UserId} version {Version}.",
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
public static class AuthzRolePermissionsChangedPermissionsProjectionConsumer
{
    public static async Task Handle(
        RolePermissionsChangedIntegrationEvent evt,
        IAuthzRolePermissionsProjectionRepository roleRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<AuthzRolePermissionsProjection> logger,
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

    private static async Task<AuthzRolePermissionsProjection> UpsertRoleProjectionAsync(
        RolePermissionsChangedIntegrationEvent evt,
        IAuthzRolePermissionsProjectionRepository roleRepository,
        CancellationToken ct
    )
    {
        var existing = await roleRepository.GetAsync(evt.TenantId, evt.RoleId, ct);
        if (existing is null)
        {
            var created = AuthzRolePermissionsProjection.Create(
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
