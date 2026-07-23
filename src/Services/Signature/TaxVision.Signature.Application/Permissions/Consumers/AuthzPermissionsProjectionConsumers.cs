using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Permissions;

namespace TaxVision.Signature.Application.Consumers;

// ---------------------------------------------------------------------------
// RBAC Fase 7 — mantiene la proyección local de permisos de AUTORIZACIÓN que consulta
// ProjectionPermissionsSource (BuildingBlocks.Web) para enforzar perm_v sin llamar a Auth por
// HTTP en el hot path de autorización. Mismo patrón que CloudStorage/Customer/Postmaster/etc.:
// idempotente por PermissionsVersion, union-recompute en RolePermissionsChanged para no perder
// permisos de un usuario multi-rol.
//
// Deliberadamente SEPARADO del consumer preexistente
// TaxVision.Signature.Application.Projections.AuthEvents.UserRolesChangedConsumer, que alimenta
// la proyección de AUDITORÍA homónima (TaxVision.Signature.Domain.Projections.UserPermissionsProjection
// — solo guarda RolesCsv para snapshots de "quién firmó/canceló", sin códigos de permiso). Wolverine
// soporta múltiples handlers para el mismo tipo de mensaje: ambos consumers reaccionan a
// UserRolesChangedIntegrationEvent de forma independiente, cada uno mantiene su propia tabla.
// ---------------------------------------------------------------------------

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

public static class AuthzRolePermissionsChangedPermissionsProjectionConsumer
{
    public static async Task Handle(
        RolePermissionsChangedIntegrationEvent evt,
        IAuthzRolePermissionsProjectionRepository roleRepository,
        IAuthzUserPermissionsProjectionRepository userRepository,
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

    /// <summary>
    /// Un usuario con VARIOS roles no puede sobrescribirse solo con los códigos del rol que
    /// cambió, o perdería los permisos heredados de sus otros roles — recompone la unión
    /// completa contra el cache de AuthzRolePermissionsProjection. <paramref name="changedRole"/>
    /// se inyecta directamente (en vez de volver a consultarlo) porque su UPDATE todavía no
    /// se guardó en esta transacción.
    /// </summary>
    private static async Task ReapplyPermissionsUnionAsync(
        Guid tenantId,
        AuthzRolePermissionsProjection changedRole,
        IReadOnlyList<AuthzUserPermissionsProjection> affectedUsers,
        IAuthzRolePermissionsProjectionRepository roleRepository,
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
