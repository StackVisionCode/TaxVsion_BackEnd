using BuildingBlocks.Authorization;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

internal static class ClientFormStaffRecipients
{
    public static async Task<IReadOnlyList<Guid>> ResolveForReviewAsync(
        Guid tenantId,
        Guid? customerId,
        Guid requestedByUserId,
        Guid? responsibleUserId,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var candidates = new HashSet<Guid>();
        if (customerId.HasValue)
        {
            var assigned = await assignments.GetAssignedUserIdsAsync(tenantId, customerId.Value, ct);
            candidates.UnionWith(assigned);
            if (await CanSeeCustomerAsync(tenantId, requestedByUserId, assigned, resolver, ct))
                candidates.Add(requestedByUserId);
            if (
                responsibleUserId.HasValue
                && await CanSeeCustomerAsync(tenantId, responsibleUserId.Value, assigned, resolver, ct)
            )
                candidates.Add(responsibleUserId.Value);
        }
        else
        {
            candidates.Add(requestedByUserId);
            if (responsibleUserId.HasValue)
                candidates.Add(responsibleUserId.Value);
        }
        return await ValidateAsync(tenantId, candidates, TasksPermissions.FormRequestsReview, resolver, ct);
    }

    public static async Task<IReadOnlyList<Guid>> ResolveRequesterForReadAsync(
        Guid tenantId,
        Guid? customerId,
        Guid requestedByUserId,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        if (customerId.HasValue)
        {
            var assigned = await assignments.GetAssignedUserIdsAsync(tenantId, customerId.Value, ct);
            if (!await CanSeeCustomerAsync(tenantId, requestedByUserId, assigned, resolver, ct))
                return [];
        }
        return await ValidateAsync(tenantId, [requestedByUserId], TasksPermissions.FormRequestsRead, resolver, ct);
    }

    // Mismo ABAC que la lista de clientes: asignado al cliente o con view_all.
    private static async Task<bool> CanSeeCustomerAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> assigned,
        IRecipientResolver resolver,
        CancellationToken ct
    ) => assigned.Contains(userId) || await HasViewAllAsync(tenantId, userId, resolver, ct);

    private static async Task<bool> HasViewAllAsync(
        Guid tenantId,
        Guid userId,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var result = await resolver.ResolveAsync(
            new Participants(tenantId, [userId]),
            new RecipientValidation(
                CustomersPermissions.ViewAll,
                PermissionModuleMap.ModuleFor(CustomersPermissions.ViewAll)
            ),
            ct
        );
        return result.IsSuccess && result.Value.Contains(userId);
    }

    private static async Task<IReadOnlyList<Guid>> ValidateAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> candidates,
        string permission,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        if (candidates.Count == 0)
            return [];
        var result = await resolver.ResolveAsync(
            new Participants(tenantId, candidates.ToArray()),
            new RecipientValidation(permission, PermissionModuleMap.ModuleFor(permission)),
            ct
        );
        return result.IsSuccess ? result.Value : [];
    }
}
