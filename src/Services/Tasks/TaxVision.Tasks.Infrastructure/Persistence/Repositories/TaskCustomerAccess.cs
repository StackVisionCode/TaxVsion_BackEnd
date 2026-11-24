using BuildingBlocks.CustomerVisibility;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

/// <summary>Con la visibilidad apagada o el permiso de ver todo, cualquier cliente; si no, los asignados.</summary>
public sealed class TaskCustomerAccess(
    ICustomerAssignmentProjectionStore assignments,
    IOptions<TasksVisibilityOptions> visibility
) : ITaskCustomerAccess
{
    public async Task<bool> CanLinkAsync(
        Guid tenantId,
        Guid userId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct
    ) =>
        !visibility.Value.Enabled
        || canViewAll
        || await assignments.IsAnyAssignedToUserAsync(tenantId, userId, [customerId], ct);
}
