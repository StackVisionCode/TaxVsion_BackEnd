using BuildingBlocks.CustomerVisibility;
using TaxVision.Campaigns.Application.Contacts.Abstractions;

namespace TaxVision.Campaigns.Infrastructure.Customers;

// Impl del lector de asignaciones sobre la proyección compartida del kit (mantenida por el consumer del
// snapshot + la reconciliación). Delega en el store del kit.
internal sealed class CampaignCustomerAssignmentReader(ICustomerAssignmentProjectionStore store)
    : ICampaignCustomerAssignmentReader
{
    public Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => store.GetAssignedCustomerIdsAsync(tenantId, userId, ct);
}
