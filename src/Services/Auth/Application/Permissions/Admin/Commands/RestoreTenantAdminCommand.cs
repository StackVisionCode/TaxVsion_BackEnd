using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Permissions.Admin.Commands;

public sealed record RestoreTenantAdminCommand(Guid UserId, Guid RequestedByUserId);

public sealed record RestoreTenantAdminResult(
    Guid UserId,
    Guid TenantId,
    bool IsOwner,
    int PermissionsVersion,
    int PermissionCodeCount,
    int RevokedSessions
);

/// <summary>
/// Break-glass de PlatformAdmin: vuelve TenantAdmin a una cuenta que quedó como PlatformAdmin dentro
/// de una oficina (cambio manual en la base, sin eventos). Deja el rol de sistema Tenant Admin, sube
/// la versión de permisos y publica <see cref="UserRolesChangedIntegrationEvent"/> con el nuevo tipo
/// de actor, así cada servicio corrige su proyección. El recálculo de asientos lo dispara el evento
/// de prioridad del dominio. Corta las sesiones: el JWT vigente todavía dice PlatformAdmin.
/// </summary>
public static class RestoreTenantAdminHandler
{
    public static async Task<Result<RestoreTenantAdminResult>> Handle(
        RestoreTenantAdminCommand command,
        IUserRepository users,
        IRoleRepository roles,
        ITenantContext tenantContext,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocations,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<RestoreTenantAdminCommand> logger,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null)
            return Result.Failure<RestoreTenantAdminResult>(new Error("User.NotFound", "User not found."));

        // Roles y permisos son de la oficina del usuario, no de quien llama (mismo criterio que reproject).
        tenantContext.SetTenant(user.TenantId);

        var restored = user.RestoreAsTenantAdmin(command.RequestedByUserId, DateTime.UtcNow);
        if (restored.IsFailure)
            return Result.Failure<RestoreTenantAdminResult>(restored.Error);

        await roles.EnsureSystemRolesCommittedAsync(user.TenantId, ct);
        var systemRole = await roles.GetSystemRoleAsync(user.TenantId, Role.SystemTenantAdmin, ct);
        if (systemRole is null)
            return Result.Failure<RestoreTenantAdminResult>(
                new Error("Role.SystemRoleMissing", "The tenant's system Tenant Admin role could not be ensured.")
            );

        await roles.ReplaceUserRolesAsync(user.Id, [systemRole.Id], command.RequestedByUserId, ct);
        user.BumpPermissionsVersion();

        var tenantRoles = await roles.GetByIdsAsync(user.TenantId, [systemRole.Id], ct);
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var deniedPermissionIds = await roles.GetDeniedPermissionIdsAsync(user.Id, ct);
        var permissionCodes = UserAccessResolver.ResolveEffectivePermissionCodes(
            tenantRoles,
            catalog,
            deniedPermissionIds
        );

        await bus.PublishAsync(
            new UserRolesChangedIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                PermissionsVersion = user.PermissionsVersion,
                RoleNames = tenantRoles.Select(role => role.Name).ToArray(),
                RoleIds = tenantRoles.Select(role => role.Id).ToArray(),
                PermissionCodes = permissionCodes,
                ActorType = user.ActorType.ToString(),
                CorrelationId = correlation.CorrelationId,
            }
        );
        user.MarkPermissionsBackfilled(DateTime.UtcNow);

        var revokedSessions = await SessionAccessCutoff.ForUserAsync(
            user,
            "actor_type_restored",
            sessions,
            denylist,
            revocations,
            ct
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.UserRestoredAsTenantAdmin,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: user.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "RestoreTenantAdmin: user {UserId} (tenant {TenantId}) restored as TenantAdmin at version {Version}, owner={IsOwner}.",
            user.Id,
            user.TenantId,
            user.PermissionsVersion,
            user.IsOwner
        );

        return Result.Success(
            new RestoreTenantAdminResult(
                user.Id,
                user.TenantId,
                user.IsOwner,
                user.PermissionsVersion,
                permissionCodes.Length,
                revokedSessions
            )
        );
    }
}
