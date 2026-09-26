using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Permissions.Abstractions;
using TaxVision.Reminder.Domain.Permissions;

namespace TaxVision.Reminder.Application.Permissions.Consumers;

// RBAC Fase 7 — mantiene la proyección local de permisos que consulta ProjectionPermissionsSource
// (BuildingBlocks.Web) para enforzar perm_v sin llamar a Auth por HTTP en el hot path de
// autorización. Copiado del shape de Notes/CloudStorage/Signature a propósito, no improvisado: los
// dos bugs reales que ya costó este consumer fueron (a) un upsert que no comparaba
// PermissionsVersion y dejaba usuarios fail-closed en silencio sin DLQ, y (b) casing
// camelCase/PascalCase al leer el evento.

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
