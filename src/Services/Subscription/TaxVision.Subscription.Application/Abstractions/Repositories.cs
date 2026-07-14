using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IPlanRepository
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default);
    Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default);
}

public interface ISubscriptionRepository
{
    Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantSubscription subscription, CancellationToken ct = default);

    /// <summary>Active subscriptions whose NextRenewalAtUtc has passed. Batch job query —
    /// crosses tenants intentionally (only the scheduler calls this, never a tenant-scoped handler).</summary>
    Task<IReadOnlyList<TenantSubscription>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetExpiredTrialsAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantSubscription>> GetRenewingBetweenAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Subscriptions with a Pending PlanChangeRequest whose EffectiveAtUtc has passed —
    /// batch job query, cross-tenant by design, only the scheduler calls this.</summary>
    Task<IReadOnlyList<TenantSubscription>> GetWithDuePlanChangeRequestsAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Admin cross-tenant query — only PlatformAdmin endpoints call this.</summary>
    Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}

public interface ISubscriptionSeatRepository
{
    Task<SubscriptionSeat?> GetByIdAsync(Guid seatId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<SubscriptionSeat?> GetByCurrentUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default);

    /// <summary>Batch job queries — cross-tenant by design, only the scheduler calls these.</summary>
    Task<IReadOnlyList<SubscriptionSeat>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<SubscriptionSeat>> GetRenewingBetweenAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Admin cross-tenant query — only PlatformAdmin endpoints call this.</summary>
    Task<(IReadOnlyList<SubscriptionSeat> Items, int TotalCount)> GetExpiredAsync(
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}

public interface ISubscriptionTenantSettingsRepository
{
    Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default);
}

public interface IAddOnDefinitionRepository
{
    Task<IReadOnlyList<AddOnDefinition>> GetPublishedAsync(CancellationToken ct = default);
    Task<AddOnDefinition?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<AddOnDefinition?> GetByIdAsync(Guid addOnDefinitionId, CancellationToken ct = default);
}

public interface ITenantAddOnRepository
{
    Task<TenantAddOn?> GetByIdAsync(Guid tenantAddOnId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<TenantAddOn>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantAddOn addOn, CancellationToken ct = default);

    /// <summary>Batch job queries — cross-tenant by design, only the scheduler calls these.</summary>
    Task<IReadOnlyList<TenantAddOn>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantAddOn>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantAddOn>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TenantAddOn>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
}

public interface ITenantEntitlementSnapshotRepository
{
    Task<TenantEntitlementSnapshot?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task UpsertAsync(TenantEntitlementSnapshot snapshot, CancellationToken ct = default);
}
