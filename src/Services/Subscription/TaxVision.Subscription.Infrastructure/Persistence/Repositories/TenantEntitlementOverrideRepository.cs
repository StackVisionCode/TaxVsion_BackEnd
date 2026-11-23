using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

// IgnoreQueryFilters + filtro explícito por TenantId: lo llaman PlatformAdmin (otra oficina) y jobs sin
// tenant en el JWT, igual que TenantAddOnRepository. Nunca sirve una request de la propia oficina.
public sealed class TenantEntitlementOverrideRepository(SubscriptionDbContext db) : ITenantEntitlementOverrideRepository
{
    public async Task<IReadOnlyList<TenantEntitlementOverride>> GetByTenantIdAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await db
            .TenantEntitlementOverrides.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .OrderBy(item => item.Key)
            .ToListAsync(ct);

    public Task<TenantEntitlementOverride?> GetForUpdateAsync(
        Guid tenantId,
        string key,
        CancellationToken ct = default
    ) =>
        db
            .TenantEntitlementOverrides.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Key == key, ct);

    public async Task AddAsync(TenantEntitlementOverride item, CancellationToken ct = default) =>
        await db.TenantEntitlementOverrides.AddAsync(item, ct);

    public void Remove(TenantEntitlementOverride item) => db.TenantEntitlementOverrides.Remove(item);

    public async Task<IReadOnlyList<TenantEntitlementOverride>> ListExpiryPendingAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    ) =>
        await db
            .TenantEntitlementOverrides.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.ExpiresAtUtc != null && item.ExpiresAtUtc <= nowUtc && item.ExpiryAppliedAtUtc == null)
            .OrderBy(item => item.ExpiresAtUtc)
            .Take(take)
            .ToListAsync(ct);
}
