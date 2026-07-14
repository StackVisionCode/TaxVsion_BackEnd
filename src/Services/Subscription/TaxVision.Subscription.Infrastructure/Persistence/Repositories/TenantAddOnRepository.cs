using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class TenantAddOnRepository(SubscriptionDbContext db) : ITenantAddOnRepository
{
    public Task<TenantAddOn?> GetByIdAsync(Guid tenantAddOnId, Guid tenantId, CancellationToken ct = default) =>
        WithRenewals(db.TenantAddOns)
            .FirstOrDefaultAsync(addOn => addOn.Id == tenantAddOnId && addOn.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<TenantAddOn>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantAddOns.AsNoTracking().Where(addOn => addOn.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(TenantAddOn addOn, CancellationToken ct = default) =>
        await db.TenantAddOns.AddAsync(addOn, ct);

    public async Task<IReadOnlyList<TenantAddOn>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await WithRenewals(db.TenantAddOns)
            .Where(addOn =>
                addOn.Status == AddOnStatus.Active
                && addOn.AutoRenew
                && addOn.NextRenewalAtUtc != null
                && addOn.NextRenewalAtUtc <= nowUtc
            )
            .OrderBy(addOn => addOn.NextRenewalAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantAddOn>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .TenantAddOns.Where(addOn =>
                addOn.Status == AddOnStatus.GracePeriod
                && addOn.GracePeriodEndsAtUtc != null
                && addOn.GracePeriodEndsAtUtc <= nowUtc
            )
            .OrderBy(addOn => addOn.GracePeriodEndsAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantAddOn>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .TenantAddOns.Where(addOn =>
                addOn.Status == AddOnStatus.Suspended
                && addOn.SuspendedAtUtc != null
                && addOn.SuspendedAtUtc <= cutoffUtc
            )
            .OrderBy(addOn => addOn.SuspendedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantAddOn>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .TenantAddOns.Where(addOn => addOn.Status == AddOnStatus.Cancelled && addOn.CurrentPeriodEndUtc <= nowUtc)
            .OrderBy(addOn => addOn.CurrentPeriodEndUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    private static IQueryable<TenantAddOn> WithRenewals(IQueryable<TenantAddOn> query) =>
        query.Include(addOn => addOn.Renewals);
}
