using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class TenantSubscriptionRepository(SubscriptionDbContext db) : ISubscriptionRepository
{
    public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        WithRenewals(db.Subscriptions).FirstOrDefaultAsync(subscription => subscription.TenantId == tenantId, ct);

    public async Task AddAsync(TenantSubscription subscription, CancellationToken ct = default) =>
        await db.Subscriptions.AddAsync(subscription, ct);

    public async Task<IReadOnlyList<TenantSubscription>> GetDueForRenewalAsync(DateTime nowUtc, int batchSize, CancellationToken ct = default) =>
        await WithRenewals(db.Subscriptions)
            .Where(s => s.Status == SubscriptionStatus.Active && s.NextRenewalAtUtc != null && s.NextRenewalAtUtc <= nowUtc)
            .OrderBy(s => s.NextRenewalAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantSubscription>> GetExpiredTrialsAsync(DateTime nowUtc, int batchSize, CancellationToken ct = default) =>
        await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndsAtUtc != null && s.TrialEndsAtUtc <= nowUtc)
            .OrderBy(s => s.TrialEndsAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantSubscription>> GetPastGracePeriodAsync(DateTime nowUtc, int batchSize, CancellationToken ct = default) =>
        await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.GracePeriod && s.GracePeriodEndsAtUtc != null && s.GracePeriodEndsAtUtc <= nowUtc)
            .OrderBy(s => s.GracePeriodEndsAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default) =>
        await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Suspended && s.SuspendedAtUtc != null && s.SuspendedAtUtc <= cutoffUtc)
            .OrderBy(s => s.SuspendedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantSubscription>> GetCancelledPastPeriodEndAsync(DateTime nowUtc, int batchSize, CancellationToken ct = default) =>
        await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Cancelled && s.CurrentPeriodEndUtc <= nowUtc)
            .OrderBy(s => s.CurrentPeriodEndUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantSubscription>> GetRenewingBetweenAsync(
        DateTime fromUtc, DateTime toUtc, int batchSize, CancellationToken ct = default) =>
        await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.NextRenewalAtUtc != null
                && s.NextRenewalAtUtc >= fromUtc && s.NextRenewalAtUtc <= toUtc)
            .OrderBy(s => s.NextRenewalAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Subscriptions.AsNoTracking().Where(s => s.Status == SubscriptionStatus.PastDue);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.NextRenewalAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    private static IQueryable<TenantSubscription> WithRenewals(IQueryable<TenantSubscription> query) =>
        query.Include(s => s.Renewals);
}
