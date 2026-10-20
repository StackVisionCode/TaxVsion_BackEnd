using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

// IgnoreQueryFilters() explícito (G8): consumers de Wolverine y job de reconciliación sin TenantContext.
public sealed class CustomerPortalAccountProjectionRepository(NotificationDbContext db)
    : ICustomerPortalAccountProjectionRepository
{
    public async Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default)
    {
        var users = await db
            .CustomerPortalAccountProjections.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.CustomerId == customerId && p.IsActive)
            .Select(p => (Guid?)p.UserId)
            .ToListAsync(ct);
        return users.FirstOrDefault();
    }

    public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        db
            .CustomerPortalAccountProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.CustomerId == customerId, ct);

    public Task<CustomerPortalAccountProjection?> GetByUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        db
            .CustomerPortalAccountProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default) =>
        await db.CustomerPortalAccountProjections.AddAsync(projection, ct);
}
