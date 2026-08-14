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

public static class RolePermissionsChangedPermissionsProjectionConsumer
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
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
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
    /// Un usuario con VARIOS roles no puede sobrescribirse solo con los códigos del rol que cambió,
    /// o perdería los permisos heredados de sus otros roles — recompone la unión completa contra el
    /// cache de RolePermissionsProjection. <paramref name="changedRole"/> se inyecta directamente
    /// (en vez de volver a consultarlo) porque su UPDATE todavía no se guardó en esta transacción.
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
