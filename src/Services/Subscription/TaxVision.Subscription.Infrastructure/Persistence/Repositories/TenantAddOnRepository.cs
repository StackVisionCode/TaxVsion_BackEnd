using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class TenantAddOnRepository(SubscriptionDbContext db) : ITenantAddOnRepository
{
    public Task<TenantAddOn?> GetByIdAsync(Guid tenantAddOnId, Guid tenantId, CancellationToken ct = default) =>
        db.TenantAddOns.FirstOrDefaultAsync(addOn => addOn.Id == tenantAddOnId && addOn.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<TenantAddOn>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantAddOns.AsNoTracking().Where(addOn => addOn.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(TenantAddOn addOn, CancellationToken ct = default) =>
        await db.TenantAddOns.AddAsync(addOn, ct);
}
