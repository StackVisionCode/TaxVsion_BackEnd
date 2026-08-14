using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Directory.Abstractions;

public interface ICustomerEmailDirectoryRepository
{
    Task<CustomerEmailDirectoryEntry?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    );

    Task AddAsync(CustomerEmailDirectoryEntry entry, CancellationToken ct = default);
}
