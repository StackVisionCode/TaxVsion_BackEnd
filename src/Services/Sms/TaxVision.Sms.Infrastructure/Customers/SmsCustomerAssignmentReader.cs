using BuildingBlocks.CustomerVisibility;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Infrastructure.Customers;

// Delega en el store del kit compartido, que mantienen el consumer del snapshot y la reconciliación.
internal sealed class SmsCustomerAssignmentReader(ICustomerAssignmentProjectionStore store)
    : ISmsCustomerAssignmentReader
{
    public Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => store.GetAssignedCustomerIdsAsync(tenantId, userId, ct);
}
