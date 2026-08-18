using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class CustomerEmailDirectoryRepository(NotificationDbContext context) : ICustomerEmailDirectoryRepository
{
    /// <summary>
    /// <c>IgnoreQueryFilters()</c> con el tenant explícito: los consumers corren en el scope de
    /// Wolverine, sin actor autenticado, y el filtro global fail-closed devolvería cero filas.
    /// </summary>
    public Task<CustomerEmailDirectoryEntry?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        context
            .CustomerEmailDirectoryEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.CustomerId == customerId, ct);

    public async Task AddAsync(CustomerEmailDirectoryEntry entry, CancellationToken ct = default) =>
        await context.CustomerEmailDirectoryEntries.AddAsync(entry, ct);
}
