using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Permissions.Commands;

/// <summary>
/// Replaces a user's per-user permission deny set (the RBAC deny layer). The user's effective
/// permissions become the union of what their roles grant, minus these denies (a deny always wins).
/// Deny-only by design: to GRANT a permission you assign a role — this command never adds permissions.
/// Idempotent: the given set fully replaces the previous one (an empty set removes every override).
/// </summary>
public sealed record SetUserPermissionOverridesCommand(
    Guid TenantId,
    Guid TargetUserId,
    IReadOnlyList<Guid> DeniedPermissionIds,
    Guid RequestedByUserId
);

/// <summary>
/// Validates and applies the deny set: tenant isolation, actor-type coherence and anti self-lockout,
/// then replaces the denies, bumps the user's permission version and re-publishes the recomputed
/// effective permission codes on <see cref="UserRolesChangedIntegrationEvent"/> so the ~20 downstream
/// projections converge — no new event, no downstream change. Publish-before-save via the outbox.
/// </summary>
public static class SetUserPermissionOverridesHandler
{
    public static async Task<Result> Handle(
        SetUserPermissionOverridesCommand command,
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
        // Anti-lockout: an admin can never touch their OWN deny set. Because the acting admin therefore
        // always keeps their access-management permission, the tenant can never be left without an admin.
        // Same "User.SelfAction" contract that AssignUserRolesHandler/DeactivateUserHandler already use.
        if (command.TargetUserId == command.RequestedByUserId)
            return Result.Failure(new Error("User.SelfAction", "You cannot change your own permission overrides."));

        var target = await users.GetByIdAsync(command.TargetUserId, ct);
        if (target is null || target.TenantId != command.TenantId)
            return Result.Failure(new Error("User.NotFound", "User does not exist in this tenant."));

        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        var validPermissionIds = catalog.Select(permission => permission.Id).ToHashSet();

        // Keep only real catalog permissions — a deny on an unknown id is dropped, never stored.
        var deniedIds = (command.DeniedPermissionIds ?? []).Distinct().Where(validPermissionIds.Contains).ToList();

        // Actor-type coherence: a deny may only target a permission valid for this seat's actor type
        // (a staff seat can neither carry nor be denied a customer-portal-only permission, and vice versa).
        var actorTypeGuard = ActorTypeRoleGuard.ValidatePermissionsForActorType(target.ActorType, deniedIds, catalog);
        if (actorTypeGuard.IsFailure)
            return actorTypeGuard;

        // The user's roles do not change here; recompute effective codes = (active role permissions) −
        // denies in memory (a DB query would read the pre-SaveChanges deny set). Same active-role rule as
        // the read path (IRoleRepository.GetEffectivePermissionCodesAsync).
        var activeRoles = (await roles.GetUserRolesAsync(target.Id, ct)).Where(role => role.IsActive).ToList();

        // The audited "what": the exact codes now denied (empty = every override restored).
        var codeByPermissionId = catalog.ToDictionary(permission => permission.Id, permission => permission.Code);
        var deniedCodes = deniedIds.Select(id => codeByPermissionId[id]).OrderBy(code => code).ToArray();

        await roles.ReplaceUserDeniesAsync(target.Id, deniedIds, command.RequestedByUserId, ct);
        target.BumpPermissionsVersion();

        await bus.PublishAsync(
            new UserRolesChangedIntegrationEvent
            {
                TenantId = target.TenantId,
                UserId = target.Id,
                PermissionsVersion = target.PermissionsVersion,
                RoleNames = activeRoles.Select(role => role.Name).ToArray(),
                RoleIds = activeRoles.Select(role => role.Id).ToArray(),
                PermissionCodes = UserAccessResolver.ResolveEffectivePermissionCodes(activeRoles, catalog, deniedIds),
                ActorType = target.ActorType.ToString(),
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.UserPermissionOverridesChanged,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "User",
                targetId: target.Id,
                detailsJson: System.Text.Json.JsonSerializer.Serialize(
                    new { deniedPermissionCount = deniedCodes.Length, deniedPermissionCodes = deniedCodes }
                )
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
