using BuildingBlocks.Authorization;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.RateLimiting.Abstractions;

namespace TaxVision.Notification.Infrastructure.Permissions;

public sealed class RecipientResolver(
    INotificationRecipientPermissionsProjectionRepository userPermissions,
    ICustomerAssignmentProjectionStore assignments,
    ITenantPlanCodeProjectionRepository tenantPlans,
    ITenantOwnerProjectionRepository tenantOwners,
    ICustomerPortalAccountProjectionRepository portalAccounts
) : IRecipientResolver
{
    public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
        userPermissions.FindActiveUserIdsByPermissionAsync(audience.TenantId, audience.PermissionCode, ct);

    public async Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
        NotificationAudience audience,
        RecipientValidation validation,
        CancellationToken ct = default
    )
    {
        var candidates = await ResolveCandidatesAsync(audience, ct);
        if (candidates.IsFailure)
            return candidates;

        var moduleGate = await IsModuleEnabledAsync(TenantIdOf(audience), validation.RequiredModule, ct);
        if (!moduleGate)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error(
                    "Notification.Audience.ModuleDisabled",
                    $"Tenant module {validation.RequiredModule} is disabled."
                )
            );

        var validated = await ValidateAsync(TenantIdOf(audience), candidates.Value, audience, validation, ct);
        return Result.Success(validated);
    }

    private async Task<IReadOnlyList<Guid>> ValidateAsync(
        Guid tenantId,
        IReadOnlyList<Guid> candidates,
        NotificationAudience audience,
        RecipientValidation validation,
        CancellationToken ct
    )
    {
        var withoutActor = candidates
            .Where(id => validation.ExcludeActorUserId is not { } actor || id != actor)
            .Distinct()
            .ToArray();
        if (withoutActor.Length == 0)
            return [];

        // ByPermission ya resolvió activo + capacidad en su query; el resto hay que validarlo contra la proyección.
        if (audience is ByPermission && validation.RequiredCapability is null)
            return withoutActor;

        return await userPermissions.FilterActiveWithCapabilityAsync(
            tenantId,
            withoutActor,
            validation.RequiredCapability,
            ct
        );
    }

    private async Task<bool> IsModuleEnabledAsync(Guid tenantId, string? requiredModule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requiredModule))
            return true;
        var plan = await tenantPlans.GetAsync(tenantId, ct);
        // Sin fila de plan = fail-open (no sabemos): no bloqueamos el aviso por una proyección aún no sembrada.
        if (plan is null)
            return true;
        return plan.EnabledModules.Contains(requiredModule, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Result<IReadOnlyList<Guid>>> ResolveCandidatesAsync(
        NotificationAudience audience,
        CancellationToken ct
    ) =>
        audience switch
        {
            ExplicitRecipient explicitRecipient => ResolveExplicit(explicitRecipient),
            ByPermission byPermission => Ok(await ResolveAsync(byPermission, ct)),
            Participants participants => Ok(participants.UserIds),
            MailboxAudience mailbox => await ResolveMailboxAsync(mailbox, ct),
            CustomerPortal portal => await ResolveCustomerPortalAsync(portal, ct),
            TenantOwnerAudience owner => await ResolveOwnerAsync(owner, ct),
            _ => Result.Failure<IReadOnlyList<Guid>>(
                new Error("Notification.Audience.Unsupported", $"Unsupported audience {audience.GetType().Name}.")
            ),
        };

    private async Task<Result<IReadOnlyList<Guid>>> ResolveCustomerPortalAsync(
        CustomerPortal portal,
        CancellationToken ct
    )
    {
        var userId = await portalAccounts.GetActivePortalUserIdAsync(portal.TenantId, portal.CustomerId, ct);
        return userId is { } id
            ? Ok(new[] { id })
            : Result.Failure<IReadOnlyList<Guid>>(
                new Error("Notification.Audience.NoPortalAccount", "Customer has no active portal account.")
            );
    }

    private async Task<Result<IReadOnlyList<Guid>>> ResolveOwnerAsync(TenantOwnerAudience owner, CancellationToken ct)
    {
        var userId = await tenantOwners.GetActiveOwnerUserIdAsync(owner.TenantId, ct);
        return userId is { } id
            ? Ok(new[] { id })
            : Result.Failure<IReadOnlyList<Guid>>(
                new Error("Notification.Audience.NoOwner", "Tenant has no projected active owner.")
            );
    }

    private async Task<Result<IReadOnlyList<Guid>>> ResolveMailboxAsync(MailboxAudience mailbox, CancellationToken ct)
    {
        if (mailbox.OwnerUserId is { } owner)
            return Ok(new[] { owner });

        // Política B — buzón de oficina = union de preparadores asignados al customer + admins con
        // connectors.accounts.office.read. El preparador asignado se entera para trabajar; el admin
        // se entera porque es el dueño del buzón de oficina y no puede ser bypassed por asignación.
        var assigned = await assignments.GetAssignedUserIdsAsync(mailbox.TenantId, mailbox.CustomerId, ct);
        var activeAdmins = await tenantOwners.GetActiveAdminUserIdsAsync(mailbox.TenantId, ct);
        var officeReaders = await userPermissions.FindActiveUserIdsByPermissionAsync(
            mailbox.TenantId,
            ConnectorsPermissions.AccountsOfficeRead,
            ct
        );
        var authorizedAdmins = activeAdmins.Intersect(officeReaders);
        var recipients = assigned.Concat(authorizedAdmins).Distinct().ToArray();
        return recipients.Length > 0
            ? Ok(recipients)
            : Result.Failure<IReadOnlyList<Guid>>(
                new Error("Notification.Audience.NoRecipient", "Office mailbox has no authorized recipient.")
            );
    }

    private static Result<IReadOnlyList<Guid>> ResolveExplicit(ExplicitRecipient recipient) =>
        recipient.UserId is { } userId
            ? Ok(new[] { userId })
            : Result.Failure<IReadOnlyList<Guid>>(
                new Error("Notification.Audience.NoUserId", "Explicit recipient has no user id for an in-app channel.")
            );

    private static Guid TenantIdOf(NotificationAudience audience) =>
        audience switch
        {
            ByPermission b => b.TenantId,
            Participants p => p.TenantId,
            MailboxAudience m => m.TenantId,
            CustomerPortal c => c.TenantId,
            TenantOwnerAudience o => o.TenantId,
            ExplicitRecipient e => e.TenantId,
            _ => Guid.Empty,
        };

    private static Result<IReadOnlyList<Guid>> Ok(IReadOnlyList<Guid> ids) =>
        Result.Success<IReadOnlyList<Guid>>(ids.Where(id => id != Guid.Empty).Distinct().ToArray());
}
