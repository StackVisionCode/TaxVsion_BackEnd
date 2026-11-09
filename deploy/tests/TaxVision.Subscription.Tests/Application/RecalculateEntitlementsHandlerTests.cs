using BuildingBlocks.Caching;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// The seat math behind the effective staff-user cap published on
/// <c>TenantEntitlementsChangedIntegrationEvent.MaxStaffUsers</c> (= plan <c>seats.max</c> included +
/// purchased STAFF seats). Only <see cref="SeatType.Standard"/> seats in a non-terminal status add to the
/// tenant's user cap — other seat pools (Portal/Signature/ReadOnly/ServiceAccount) never do, so a client
/// (portal) seat can't silently raise the staff cap.
/// </summary>
public sealed class RecalculateEntitlementsHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);

    private static SubscriptionSeat Seat(SeatType type) =>
        SubscriptionSeat
            .Purchase(
                Tenant,
                type,
                SeatSourceType.Manual,
                null,
                Money.Zero("USD"),
                BillingCycle.Monthly,
                false,
                Actor,
                Now
            )
            .Value;

    [Fact]
    public void CountActiveStaffSeats_counts_only_non_terminal_standard_seats()
    {
        var cancelledStandard = Seat(SeatType.Standard);
        Assert.True(cancelledStandard.CancelBeforeActivation("test", Actor, Now).IsSuccess);

        var seats = new List<SubscriptionSeat>
        {
            Seat(SeatType.Standard), // counts
            Seat(SeatType.Standard), // counts
            Seat(SeatType.Portal), // client pool — never counts against staff
            Seat(SeatType.Signature), // separate pool — never counts
            cancelledStandard, // terminal — excluded
        };

        Assert.Equal(2, RecalculateEntitlementsHandler.CountActiveStaffSeats(seats));
    }

    [Fact]
    public void CountActiveStaffSeats_excludes_suspended_seats()
    {
        // Suspended = agotamos reintentos de cobro; ya nadie paga ese asiento. Mantenerlo dentro del
        // cupo dejaría al tenant invitando empleados a un asiento sin pagador — mismo criterio que
        // EntitlementSnapshotBuilder.CountSeatsAsync (que también lo excluye).
        var suspended = Seat(SeatType.Standard);
        Assert.True(suspended.SuspendForPolicyViolation("test", Actor, Now).IsSuccess);

        var seats = new List<SubscriptionSeat> { Seat(SeatType.Standard), suspended };

        Assert.Equal(1, RecalculateEntitlementsHandler.CountActiveStaffSeats(seats));
    }

    [Fact]
    public void CountActiveStaffSeats_is_zero_when_there_are_no_seats()
    {
        Assert.Equal(0, RecalculateEntitlementsHandler.CountActiveStaffSeats([]));
    }

    // SUB-4: la anti-entropía pide el recálculo de todos los tenants a diario; si el snapshot ya está al día,
    // no sube la revisión ni avisa a los consumidores.
    [Fact]
    public async Task UnchangedContent_DoesNotBumpRevisionNorPublish()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", ["customers"]);
        var subscription = AddOnTestCatalog.ActiveSubscription(Tenant, plan);
        var current = (
            await RecalculateAsync(subscription, plan, [], new FakeSnapshotRepo(), new CapturingMessageBus())
        ).Upserted!;

        var snapshots = new FakeSnapshotRepo(current);
        var bus = new CapturingMessageBus();
        await RecalculateAsync(subscription, plan, [], snapshots, bus, onlyIfChanged: true);

        Assert.Null(snapshots.Upserted);
        Assert.Empty(bus.Published.OfType<TenantEntitlementsChangedIntegrationEvent>());
    }

    [Fact]
    public async Task ChangedContent_BumpsRevisionAndPublishes_EvenWhenOnlyIfChanged()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", ["customers"]);
        var subscription = AddOnTestCatalog.ActiveSubscription(Tenant, plan);
        var stale = (
            await RecalculateAsync(subscription, plan, [], new FakeSnapshotRepo(), new CapturingMessageBus())
        ).Upserted!;
        var addOn = AddOnTestCatalog.ModuleAddOn("signature.pack", "signature");

        var snapshots = new FakeSnapshotRepo(stale);
        var bus = new CapturingMessageBus();
        await RecalculateAsync(subscription, plan, [addOn], snapshots, bus, onlyIfChanged: true);

        Assert.Equal(stale.RevisionNumber + 1, snapshots.Upserted!.RevisionNumber);
        Assert.Equal("True", snapshots.Upserted.FindByKey("module.signature")?.Value, ignoreCase: true);
        Assert.Single(bus.Published.OfType<TenantEntitlementsChangedIntegrationEvent>());
    }

    // Sin la marca (compras, admin, reposición de SUB-BB2) siempre sube la revisión y avisa, como hasta hoy.
    [Fact]
    public async Task WithoutOnlyIfChanged_AlwaysPublishes()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", ["customers"]);
        var subscription = AddOnTestCatalog.ActiveSubscription(Tenant, plan);
        var current = (
            await RecalculateAsync(subscription, plan, [], new FakeSnapshotRepo(), new CapturingMessageBus())
        ).Upserted!;

        var snapshots = new FakeSnapshotRepo(current);
        var bus = new CapturingMessageBus();
        await RecalculateAsync(subscription, plan, [], snapshots, bus);

        Assert.Equal(current.RevisionNumber + 1, snapshots.Upserted!.RevisionNumber);
        Assert.Single(bus.Published.OfType<TenantEntitlementsChangedIntegrationEvent>());
    }

    // SUB-5: el conflicto sube para que Wolverine reintente, y la revisión perdedora no se anuncia.
    [Fact]
    public async Task Conflict_IsRethrownForRetry()
    {
        var plan = AddOnTestCatalog.PlanWith("starter", ["customers"]);
        var subscription = AddOnTestCatalog.ActiveSubscription(Tenant, plan);
        var bus = new CapturingMessageBus();

        await Assert.ThrowsAsync<ConflictException>(() =>
            RecalculateAsync(subscription, plan, [], new FakeSnapshotRepo(), bus, unitOfWork: new ConflictingUnitOfWork())
        );

        Assert.Empty(bus.Published.OfType<TenantEntitlementsChangedIntegrationEvent>());
    }

    internal static async Task<FakeSnapshotRepo> RecalculateAsync(
        TenantSubscription subscription,
        SubscriptionPlan plan,
        AddOnDefinition[] addOns,
        FakeSnapshotRepo snapshots,
        CapturingMessageBus bus,
        bool onlyIfChanged = false,
        IUnitOfWork? unitOfWork = null
    )
    {
        var result = await RecalculateEntitlementsHandler.Handle(
            new RecalculateEntitlementsCommand(subscription.TenantId, onlyIfChanged),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeSeatRepo(),
            new FakeTenantAddOnRepo([.. addOns.Select(a => AddOnTestCatalog.Owned(subscription.TenantId, a))]),
            new FakeAddOnDefinitionRepository(addOns),
            snapshots,
            new NoOpCache(),
            unitOfWork ?? new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantEntitlementSnapshot>.Instance,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return snapshots;
    }

    private sealed class ConflictingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new ConflictException("Persistence.ConcurrencyConflict", "conflict");
    }

    private sealed class NoOpCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
