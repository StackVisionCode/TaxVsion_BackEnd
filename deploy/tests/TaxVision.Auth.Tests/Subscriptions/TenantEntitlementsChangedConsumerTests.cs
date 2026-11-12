using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Subscriptions.IntegrationEvents;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Tests.Subscriptions;

/// <summary>
/// Projects the tenant's plan limits from the Subscription entitlements event. The seat cap
/// (<c>TenantPlanLimits.MaxUsers</c>) must come from <c>MaxStaffUsers</c> (effective = plan included +
/// purchased staff seats) and, for an old message that lacks it, fall back to the plan's included
/// <c>seats.max</c> — NEVER to <c>SeatCount</c> (which starts at 0 and blocked every invite: the bug this
/// consumer fix resolves).
/// </summary>
public sealed class TenantEntitlementsChangedConsumerTests
{
    private static TenantEntitlementsChangedIntegrationEvent Event(
        int? maxStaffUsers,
        int seatCount,
        string seatsMax
    ) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            RevisionNumber = 1,
            ChangedKeys = [],
            PlanCode = "PRO",
            SubscriptionStatus = "Active",
            SeatCount = seatCount,
            AvailableSeatCount = 0,
            MaxStaffUsers = maxStaffUsers,
            EntitlementValues = new Dictionary<string, string> { ["seats.max"] = seatsMax },
            CorrelationId = "test-correlation",
        };

    private static async Task<int> ProjectedMaxUsersAsync(TenantEntitlementsChangedIntegrationEvent evt)
    {
        var store = new FakeTenantPlanLimitsStore();
        await TenantEntitlementsChangedConsumer.Handle(
            evt,
            store,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );
        return store.Stored!.MaxUsers;
    }

    [Fact]
    public async Task Uses_MaxStaffUsers_when_present()
    {
        // Pro includes 10, tenant bought 2 extra staff seats -> effective 12.
        Assert.Equal(12, await ProjectedMaxUsersAsync(Event(maxStaffUsers: 12, seatCount: 0, seatsMax: "10")));
    }

    [Fact]
    public async Task Falls_back_to_included_seats_max_when_MaxStaffUsers_is_absent()
    {
        // Old message (no MaxStaffUsers) with SeatCount 0 must NOT block: fall back to seats.max = 10.
        Assert.Equal(10, await ProjectedMaxUsersAsync(Event(maxStaffUsers: null, seatCount: 0, seatsMax: "10")));
    }

    [Fact]
    public async Task Never_projects_the_raw_SeatCount()
    {
        // Even with a non-zero SeatCount, the absent-MaxStaffUsers path uses seats.max, not SeatCount.
        Assert.Equal(10, await ProjectedMaxUsersAsync(Event(maxStaffUsers: null, seatCount: 5, seatsMax: "10")));
    }

    // Análisis 2026-11-11: sin guard de revisión, un evento que llega tarde bajaba los límites a un valor viejo.
    [Fact]
    public async Task An_older_revision_never_lowers_the_limits()
    {
        var store = new FakeTenantPlanLimitsStore();
        var newer = Event(maxStaffUsers: 10, seatCount: 0, seatsMax: "10") with { RevisionNumber = 5 };
        var older = newer with { RevisionNumber = 3, MaxStaffUsers = 2, PlanCode = "OLD" };

        await HandleAsync(newer, store);
        await HandleAsync(older, store);

        Assert.Equal(10, store.Stored!.MaxUsers);
        Assert.Equal("PRO", store.Stored.PlanCode);
        Assert.Equal(5, store.Stored.LastEntitlementsRevision);
    }

    [Fact]
    public async Task The_same_revision_is_applied_again_idempotently()
    {
        var store = new FakeTenantPlanLimitsStore();
        var evt = Event(maxStaffUsers: 10, seatCount: 0, seatsMax: "10") with { RevisionNumber = 5 };

        await HandleAsync(evt, store);
        await HandleAsync(evt with { MaxStaffUsers = 12 }, store);

        Assert.Equal(12, store.Stored!.MaxUsers);
    }

    private static Task HandleAsync(TenantEntitlementsChangedIntegrationEvent evt, FakeTenantPlanLimitsStore store) =>
        TenantEntitlementsChangedConsumer.Handle(
            evt,
            store,
            new FakeUnitOfWork(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    private sealed class FakeTenantPlanLimitsStore : ITenantPlanLimitsStore
    {
        public TenantPlanLimits? Stored { get; private set; }

        public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Stored);

        public Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default)
        {
            Stored = limits;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
