using BuildingBlocks.CustomerVisibility;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Permissions;

public sealed class CustomerAssignmentAudienceRepository(ICustomerAssignmentProjectionStore assignments)
    : ICustomerAssignmentAudienceRepository
{
    public Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => assignments.GetAssignedUserIdsAsync(tenantId, customerId, ct);
}
