using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Discounts;
using TaxVision.Subscription.Domain.Audit;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// El PlatformAdmin da un descuento a una oficina (retención o acuerdo), sin código: exige motivo, queda auditado con
/// el actor, no se combina con otro salvo reemplazo explícito y se puede consultar y terminar.
/// </summary>
public sealed class SubscriptionDiscountAdminTests
{
    private static readonly Guid Admin = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Admin_discount_requires_a_reason()
    {
        var f = new Fixture();

        var granted = await f.GrantAsync(reason: " ");

        Assert.Equal("Subscription.Discount.ReasonRequired", granted.Error.Code);
        Assert.Empty(f.Subscription.Discounts);
        Assert.Equal(0, f.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Grant_saves_the_discount_and_audits_the_actor()
    {
        var f = new Fixture();

        var granted = await f.GrantAsync();

        Assert.True(granted.IsSuccess, granted.IsFailure ? granted.Error.Code : null);
        Assert.Equal(2_000, granted.Value.PercentBasisPoints);
        Assert.Equal(DiscountSource.Admin, granted.Value.Source);
        Assert.Equal(1, f.UnitOfWork.Saves);
        var entry = Assert.Single(f.Audit.Entries);
        Assert.Equal("TenantSubscription.DiscountGranted", entry.Action);
        Assert.Equal(Admin, entry.ActorUserId);
        Assert.Equal("Retention deal", entry.Reason);
    }

    [Fact]
    public async Task Once_starts_at_the_next_renewal()
    {
        var f = new Fixture();

        var granted = await f.GrantAsync(duration: DiscountDuration.Once);

        Assert.Equal(f.Subscription.CurrentPeriodEndUtc, granted.Value.StartsAtUtc);
    }

    [Fact]
    public async Task A_second_discount_needs_an_explicit_replace()
    {
        var f = new Fixture();
        await f.GrantAsync();

        var conflict = await f.GrantAsync();
        var replaced = await f.GrantAsync(percent: 1_000, replace: true);

        Assert.Equal("Subscription.Discount.AlreadyActive", conflict.Error.Code);
        Assert.True(replaced.IsSuccess);
        Assert.Equal(1_000, (await f.GetAsync()).Value!.PercentBasisPoints);
    }

    [Fact]
    public async Task End_removes_the_discount_and_get_shows_none()
    {
        var f = new Fixture();
        await f.GrantAsync();

        var ended = await EndSubscriptionDiscountHandler.Handle(
            new EndSubscriptionDiscountCommand(f.Subscription.TenantId, "Deal finished", Admin),
            f.Repository,
            f.UnitOfWork,
            f.Audit,
            f.Correlation,
            CancellationToken.None
        );

        Assert.True(ended.IsSuccess);
        Assert.Null((await f.GetAsync()).Value);
        Assert.Equal("TenantSubscription.DiscountEnded", f.Audit.Entries[^1].Action);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var now = DateTime.UtcNow;
            var plan = SubscriptionPlan
                .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, now)
                .Value;
            var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
            plan.AddVersion(version, Guid.Empty, now);
            plan.PublishVersion(version.Id, now, Guid.Empty, now);
            Subscription = TenantSubscription
                .ActivateImmediately(
                    Guid.NewGuid(),
                    plan,
                    version,
                    BillingCycle.Monthly,
                    now,
                    now.AddMonths(1),
                    Guid.Empty,
                    now
                )
                .Value;
            Repository = new SingleSubscriptionRepository(Subscription);
        }

        public TenantSubscription Subscription { get; }
        public SingleSubscriptionRepository Repository { get; }
        public CountingUnitOfWork UnitOfWork { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public FixedCorrelation Correlation { get; } = new();

        public Task<BuildingBlocks.Results.Result<SubscriptionDiscountView>> GrantAsync(
            int percent = 2_000,
            DiscountDuration duration = DiscountDuration.Forever,
            string reason = "Retention deal",
            bool replace = false
        ) =>
            GrantSubscriptionDiscountHandler.Handle(
                new GrantSubscriptionDiscountCommand(
                    Subscription.TenantId,
                    percent,
                    null,
                    null,
                    duration,
                    null,
                    DiscountAppliesTo.Base,
                    reason,
                    replace,
                    Admin
                ),
                Repository,
                UnitOfWork,
                Audit,
                Correlation,
                CancellationToken.None
            );

        public Task<BuildingBlocks.Results.Result<SubscriptionDiscountView?>> GetAsync() =>
            GetSubscriptionDiscountHandler.Handle(
                new GetSubscriptionDiscountQuery(Subscription.TenantId),
                Repository,
                CancellationToken.None
            );
    }

    internal sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    internal sealed class RecordingAudit : ISubscriptionAuditLogWriter
    {
        public List<SubscriptionAuditLog> Entries { get; } = [];

        public Task AppendAsync(SubscriptionAuditLog entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    internal sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-discount";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>Solo lo que usan los handlers del descuento; el resto no participa.</summary>
    internal sealed class SingleSubscriptionRepository(TenantSubscription subscription) : ISubscriptionRepository
    {
        public Task<TenantSubscription?> GetWithFullHistoryAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(subscription.TenantId == tenantId ? subscription : null);

        public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            GetWithFullHistoryAsync(tenantId, ct);

        public Task AddAsync(TenantSubscription added, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantSubscription?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetDueForRenewalAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetExpiredTrialsAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetByStatusesAsync(
            IReadOnlyCollection<SubscriptionStatus> statuses,
            Guid afterTenantId,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetActiveWithPeriodEndedBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetCancelledPastPeriodEndAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetRenewingBetweenAsync(
            DateTime fromUtc,
            DateTime toUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetAccessEndingBetweenAsync(
            DateTime fromUtc,
            DateTime toUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetTenantIdsByPlanAsync(
            Guid planId,
            Guid afterTenantId,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
