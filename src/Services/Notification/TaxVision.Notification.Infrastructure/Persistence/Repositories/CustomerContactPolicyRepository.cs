using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class CustomerContactPolicyRepository(NotificationDbContext context) : ICustomerContactPolicyRepository
{
    /// <summary>
    /// <c>IgnoreQueryFilters()</c> + tenant explícito: igual que el resto del Directory. Los consumers
    /// corren en el scope de Wolverine sin actor autenticado, y el filtro ambiental devolvería cero.
    /// </summary>
    public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        context
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.CustomerId == customerId, ct);

    public async Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default) =>
        await context.CustomerContactPolicyProjections.AddAsync(row, ct);
}
