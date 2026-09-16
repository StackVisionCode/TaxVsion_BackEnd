using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Fakes en memoria para ejercitar el EntitlementSnapshotBuilder / RecalculateEntitlements
/// sin EF. Solo implementan lo que el builder consulta; el resto de la superficie lanza.</summary>
public sealed class FakeSubscriptionRepo(TenantSubscription? subscription) : ISubscriptionRepository
{
    public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(subscription);

    public Task AddAsync(TenantSubscription s, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<TenantSubscription?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<TenantSubscription>> GetDueForRenewalAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantSubscription>> GetExpiredTrialsAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantSubscription>> GetPastGracePeriodAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(
        DateTime c,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantSubscription>> GetCancelledPastPeriodEndAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantSubscription>> GetRenewingBetweenAsync(
        DateTime f,
        DateTime t,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
        int p,
        int s,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> GetTenantIdsByPlanAsync(Guid p, Guid a, int b, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

public sealed class FakePlanRepo(SubscriptionPlan? plan) : IPlanRepository
{
    public Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
        Task.FromResult(plan is not null && plan.Id == planId ? plan : null);

    public Task<SubscriptionPlan?> GetByIdForUpdateAsync(Guid planId, CancellationToken ct = default) =>
        Task.FromResult(plan is not null && plan.Id == planId ? plan : null);
}

public sealed class FakeSeatRepo(IReadOnlyList<SubscriptionSeat>? seats = null) : ISubscriptionSeatRepository
{
    private readonly IReadOnlyList<SubscriptionSeat> _seats = seats ?? [];

    public Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(_seats);

    public Task<SubscriptionSeat?> GetByIdAsync(Guid id, Guid t, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<SubscriptionSeat?> GetByCurrentUserIdAsync(Guid t, Guid u, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetDueForRenewalAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetPastGracePeriodAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetSuspendedBeforeAsync(
        DateTime c,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetCancelledPastPeriodEndAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<SubscriptionSeat>> GetRenewingBetweenAsync(
        DateTime f,
        DateTime t,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<(IReadOnlyList<SubscriptionSeat> Items, int TotalCount)> GetExpiredAsync(
        int p,
        int s,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

public sealed class FakeTenantAddOnRepo(IReadOnlyList<TenantAddOn>? addOns = null) : ITenantAddOnRepository
{
    private readonly IReadOnlyList<TenantAddOn> _addOns = addOns ?? [];

    public Task<IReadOnlyList<TenantAddOn>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(_addOns);

    public Task<IReadOnlyList<TenantAddOn>> GetByTenantIdForUpdateAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult(_addOns);

    public Task<TenantAddOn?> GetByIdAsync(Guid id, Guid t, CancellationToken ct = default) =>
        Task.FromResult(_addOns.FirstOrDefault(a => a.Id == id && a.TenantId == t));

    public Task AddAsync(TenantAddOn addOn, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantAddOn>> GetDueForRenewalAsync(DateTime n, int b, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<TenantAddOn>> GetPastGracePeriodAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantAddOn>> GetSuspendedBeforeAsync(
        DateTime c,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<TenantAddOn>> GetCancelledPastPeriodEndAsync(
        DateTime n,
        int b,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}

public sealed class FakeSnapshotRepo : ITenantEntitlementSnapshotRepository
{
    public TenantEntitlementSnapshot? Upserted { get; private set; }

    public Task<TenantEntitlementSnapshot?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<TenantEntitlementSnapshot?>(null);

    public Task UpsertAsync(TenantEntitlementSnapshot snapshot, CancellationToken ct = default)
    {
        Upserted = snapshot;
        return Task.CompletedTask;
    }
}
