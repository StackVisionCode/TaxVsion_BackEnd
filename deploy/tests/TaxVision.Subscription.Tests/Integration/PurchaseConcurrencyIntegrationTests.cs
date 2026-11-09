using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-3: doble clic en la compra directa. Las dos peticiones pasan la elegibilidad antes de guardar; la
/// RowVersion de TenantSubscriptions deja guardar solo a una.
/// </summary>
public sealed class PurchaseConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => _db.SeedActiveSubscriptionAsync();

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task TwoAddOnPurchasesInParallel_CreateOneAddOn()
    {
        var definition = AddOnTestCatalog.QuotaAddOn("test.single", allowMultiple: false);
        var gate = new AsyncGate(2);
        var busA = new CapturingMessageBus();
        var busB = new CapturingMessageBus();

        var outcomes = await Task.WhenAll(PurchaseAddOnAsync(definition, gate, busA), PurchaseAddOnAsync(definition, gate, busB));

        Assert.Single(outcomes, o => o.Error is null && o.Result!.IsSuccess);
        var loser = Assert.Single(outcomes, o => o.Error is not null);
        Assert.IsType<ConflictException>(loser.Error);
        var loserBus = ReferenceEquals(loser, outcomes[0]) ? busA : busB;
        Assert.Empty(loserBus.Published.OfType<AddOnRenewalDueIntegrationEvent>());

        await using var check = _db.NewContext();
        Assert.Equal(
            1,
            await check
                .TenantAddOns.IgnoreQueryFilters()
                .CountAsync(a => a.TenantId == _db.TenantId && a.Status == AddOnStatus.Active)
        );
    }

    [Fact]
    public async Task TwoSeatPurchasesInParallel_RespectMaxSeatsAllowed()
    {
        await SeedSeatCapAsync(maxSeats: 3, activeSeats: 1);
        var gate = new AsyncGate(2);

        var outcomes = await Task.WhenAll(PurchaseSeatsAsync(gate), PurchaseSeatsAsync(gate));

        Assert.Single(outcomes, o => o.Error is null && o.Result!.IsSuccess);
        Assert.IsType<ConflictException>(Assert.Single(outcomes, o => o.Error is not null).Error);

        await using var check = _db.NewContext();
        Assert.Equal(
            3,
            await check
                .Seats.IgnoreQueryFilters()
                .CountAsync(s =>
                    s.TenantId == _db.TenantId && s.Status != SeatStatus.Cancelled && s.Status != SeatStatus.Expired
                )
        );
    }

    // SUB-12: dos peticiones simultáneas con la misma clave; el reintento del perdedor ve la compra hecha.
    [Fact]
    public async Task TwoPurchasesWithSameClientKey_CreateOneAddOnAndChargeOnce()
    {
        var definition = AddOnTestCatalog.QuotaAddOn("test.multi", allowMultiple: true);
        var gate = new AsyncGate(2);
        var bus = new CapturingMessageBus();

        var outcomes = await Task.WhenAll(
            PurchaseAddOnAsync(definition, gate, bus, "key-1"),
            PurchaseAddOnAsync(definition, gate, bus, "key-1")
        );

        Assert.Single(outcomes, o => o.Error is null && o.Result!.IsSuccess);
        var retry = await PurchaseAddOnAsync(definition, new AsyncGate(1), bus, "key-1");

        Assert.True(retry.Result!.IsSuccess);
        var charge = Assert.Single(bus.Published.OfType<AddOnRenewalDueIntegrationEvent>());
        Assert.Equal(retry.Result.Value, charge.TenantAddOnId);
        await using var check = _db.NewContext();
        Assert.Equal(1, await check.TenantAddOns.IgnoreQueryFilters().CountAsync(a => a.TenantId == _db.TenantId));
    }

    private async Task<Outcome<Guid>> PurchaseAddOnAsync(
        AddOnDefinition definition,
        AsyncGate gate,
        CapturingMessageBus bus,
        string? clientKey = null
    )
    {
        await Task.Yield();
        await using var db = _db.NewContext();
        try
        {
            var result = await PurchaseAddOnHandler.Handle(
                new PurchaseAddOnCommand(_db.TenantId, definition.Code.Value, 1, AutoRenew: true, Guid.NewGuid(), clientKey),
                new TaxVision.Subscription.Application.AddOns.AddOnPurchaseEligibility(new TenantSubscriptionRepository(db), new FakePlanRepo(null), new FakeModuleRepo(), new FakeAddOnDefinitionRepository(definition), new SubscriptionTenantSettingsRepository(db), new GatedTenantAddOnRepo(new TenantAddOnRepository(db), gate)),
                new GatedTenantAddOnRepo(new TenantAddOnRepository(db), gate),
                db,
                bus,
                new FakeCorrelationContext(),
                new FakeSubscriptionAuditLogWriter(),
                new FakeSubscriptionMetrics(),
                NullLogger<TenantAddOn>.Instance,
                CancellationToken.None
            );
            return new Outcome<Guid>(result, null);
        }
        catch (Exception ex)
        {
            return new Outcome<Guid>(null, ex);
        }
    }

    private async Task<Outcome<IReadOnlyList<Guid>>> PurchaseSeatsAsync(AsyncGate gate)
    {
        await Task.Yield();
        await using var db = _db.NewContext();
        try
        {
            var result = await PurchaseSeatsHandler.Handle(
                new PurchaseSeatsCommand(_db.TenantId, "Standard", 2, AutoRenew: true, Guid.NewGuid()),
                new TenantSubscriptionRepository(db),
                new GatedSeatRepo(new SubscriptionSeatRepository(db), gate),
                new SubscriptionTenantSettingsRepository(db),
                new FakeSeatPricingRepository(SeatPricingUsd()),
                db,
                new CapturingMessageBus(),
                new FakeCorrelationContext(),
                new FakeSubscriptionAuditLogWriter(),
                new FakeSubscriptionMetrics(),
                NullLogger<SubscriptionSeat>.Instance,
                CancellationToken.None
            );
            return new Outcome<IReadOnlyList<Guid>>(result, null);
        }
        catch (Exception ex)
        {
            return new Outcome<IReadOnlyList<Guid>>(null, ex);
        }
    }

    private async Task SeedSeatCapAsync(int maxSeats, int activeSeats)
    {
        await using var db = _db.NewContext();
        var nowUtc = DateTime.UtcNow;
        var settings = SubscriptionTenantSettings.Default(_db.TenantId, Guid.Empty, nowUtc).Value;
        Assert.True(settings.ApplyPatch(new SubscriptionSettingsPatch(MaxSeatsAllowed: maxSeats), Guid.Empty, nowUtc).IsSuccess);
        db.TenantSettings.Add(settings);
        var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == _db.TenantId);
        for (var i = 0; i < activeSeats; i++)
        {
            var seat = SubscriptionSeat
                .Purchase(
                    _db.TenantId,
                    SeatType.Standard,
                    SeatSourceType.Plan,
                    subscription.PlanId,
                    Money.Create(15m, "USD").Value,
                    BillingCycle.Monthly,
                    true,
                    Guid.Empty,
                    nowUtc
                )
                .Value;
            seat.Activate(nowUtc, subscription.CurrentPeriodEndUtc, Guid.Empty, nowUtc);
            db.Seats.Add(seat);
        }
        await db.SaveChangesAsync();
    }

    private static SeatPricing SeatPricingUsd()
    {
        var pricing = SeatPricing.Seed(Guid.NewGuid(), DateTime.UtcNow);
        pricing.AddPriceTier(
            SeatPriceTier.Create(pricing.Id, SeatType.Standard, BillingCycle.Monthly, Money.Create(15m, "USD").Value).Value
        );
        return pricing;
    }

    private sealed record Outcome<T>(Result<T>? Result, Exception? Error);

    /// <summary>Suelta a todos cuando llegan los <paramref name="participants"/>: ambos pasaron la elegibilidad.</summary>
    private sealed class AsyncGate(int participants)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == participants)
                _open.TrySetResult();
            return _open.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class GatedTenantAddOnRepo(ITenantAddOnRepository inner, AsyncGate gate) : ITenantAddOnRepository
    {
        public Task<TenantAddOn?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            inner.GetByIdAsync(id, tenantId, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            inner.GetByTenantIdAsync(tenantId, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetByTenantIdForUpdateAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => inner.GetByTenantIdForUpdateAsync(tenantId, ct);

        public async Task AddAsync(TenantAddOn addOn, CancellationToken ct = default)
        {
            await gate.ArriveAsync();
            await inner.AddAsync(addOn, ct);
        }

        public Task<IReadOnlyList<TenantAddOn>> GetDueForRenewalAsync(DateTime n, int b, CancellationToken ct = default) =>
            inner.GetDueForRenewalAsync(n, b, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetSuspendedBeforeAsync(DateTime c, int b, CancellationToken ct = default) =>
            inner.GetSuspendedBeforeAsync(c, b, ct);

        public Task<IReadOnlyList<TenantAddOn>> GetCancelledPastPeriodEndAsync(
            DateTime n,
            int b,
            CancellationToken ct = default
        ) => inner.GetCancelledPastPeriodEndAsync(n, b, ct);
    }

    private sealed class GatedSeatRepo(ISubscriptionSeatRepository inner, AsyncGate gate) : ISubscriptionSeatRepository
    {
        private bool _arrived;

        public Task<SubscriptionSeat?> GetByIdAsync(Guid seatId, Guid tenantId, CancellationToken ct = default) =>
            inner.GetByIdAsync(seatId, tenantId, ct);

        public Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            inner.GetByTenantIdAsync(tenantId, ct);

        public async Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default)
        {
            if (!_arrived)
            {
                _arrived = true;
                await gate.ArriveAsync();
            }
            await inner.AddAsync(seat, ct);
        }

        public Task<IReadOnlyList<SubscriptionSeat>> GetDueForRenewalAsync(DateTime n, int b, CancellationToken ct = default) =>
            inner.GetDueForRenewalAsync(n, b, ct);

        public Task<IReadOnlyList<SubscriptionSeat>> GetSuspendedBeforeAsync(
            DateTime c,
            int b,
            CancellationToken ct = default
        ) => inner.GetSuspendedBeforeAsync(c, b, ct);

        public Task<IReadOnlyList<SubscriptionSeat>> GetCancelledPastPeriodEndAsync(
            DateTime n,
            int b,
            CancellationToken ct = default
        ) => inner.GetCancelledPastPeriodEndAsync(n, b, ct);

        public Task<IReadOnlyList<SubscriptionSeat>> GetRenewingBetweenAsync(
            DateTime from,
            DateTime to,
            int b,
            CancellationToken ct = default
        ) => inner.GetRenewingBetweenAsync(from, to, b, ct);

        public Task<(IReadOnlyList<SubscriptionSeat> Items, int TotalCount)> GetExpiredAsync(
            int p,
            int s,
            CancellationToken ct = default
        ) => inner.GetExpiredAsync(p, s, ct);
    }
}
