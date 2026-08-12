using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Inventory.Application.Permissions.Abstractions;
using TaxVision.Inventory.Domain.Permissions;

namespace TaxVision.Inventory.Application.Permissions.Consumers;

// RBAC Fase 7 — proyección local de permisos (mismo patrón que Sms/Catalog). Registro explícito por tipo.

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
        using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
        {
            var existing = await repository.GetAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
            {
                await repository.AddAsync(UserPermissionsProjection.Create(evt.TenantId, evt.UserId, evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds), ct);
                logger.LogInformation("UserPermissionsProjection created for {UserId} version {Version}.", evt.UserId, evt.PermissionsVersion);
            }
            else
            {
                existing.ApplyIfNewer(evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds);
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

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
        using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
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
            logger.LogInformation("RolePermissionsChanged: recomputed union for {Count} user(s) of role {RoleId}.", affectedUsers.Count, evt.RoleId);
        }
    }

    private static async Task<RolePermissionsProjection> UpsertRoleProjectionAsync(RolePermissionsChangedIntegrationEvent evt, IRolePermissionsProjectionRepository roleRepository, CancellationToken ct)
    {
        var existing = await roleRepository.GetAsync(evt.TenantId, evt.RoleId, ct);
        if (existing is null)
        {
            var created = RolePermissionsProjection.Create(evt.TenantId, evt.RoleId, evt.RoleName, evt.PermissionsVersion, evt.PermissionCodes);
            await roleRepository.AddAsync(created, ct);
            return created;
        }
        existing.ApplyIfNewer(evt.RoleName, evt.PermissionsVersion, evt.PermissionCodes);
        return existing;
    }

    private static async Task ReapplyPermissionsUnionAsync(Guid tenantId, RolePermissionsProjection changedRole, IReadOnlyList<UserPermissionsProjection> affectedUsers, IRolePermissionsProjectionRepository roleRepository, CancellationToken ct)
    {
        var otherRoleIds = affectedUsers.SelectMany(u => u.RoleIds()).Where(r => r != changedRole.Id).Distinct().ToList();
        var otherRoles = otherRoleIds.Count == 0 ? [] : await roleRepository.FindByRoleIdsAsync(tenantId, otherRoleIds, ct);
        var rolesById = otherRoles.ToDictionary(r => r.Id, r => r);
        rolesById[changedRole.Id] = changedRole;

        foreach (var user in affectedUsers)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleId in user.RoleIds())
                if (rolesById.TryGetValue(roleId, out var role))
                    foreach (var code in role.PermissionCodes())
                        union.Add(code);
            user.ReapplyPermissionsUnion(union);
        }
    }
}
