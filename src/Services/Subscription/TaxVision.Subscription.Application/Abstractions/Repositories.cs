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
}

public interface ISubscriptionSeatRepository
{
    Task<SubscriptionSeat?> GetByIdAsync(Guid seatId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<SubscriptionSeat?> GetByCurrentUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default);
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
}

public interface ITenantEntitlementSnapshotRepository
{
    Task<TenantEntitlementSnapshot?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task UpsertAsync(TenantEntitlementSnapshot snapshot, CancellationToken ct = default);
}
