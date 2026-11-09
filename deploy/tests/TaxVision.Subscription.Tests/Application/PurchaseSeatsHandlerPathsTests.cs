using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>SUB-11: caracteriza los rechazos y el cargo exacto de la compra de asientos antes de mover las reglas.</summary>
public sealed class PurchaseSeatsHandlerPathsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task The_quantity_must_be_in_range(int quantity)
    {
        var world = new World();

        var result = await world.PurchaseAsync("Standard", quantity);

        world.AssertRefused(result, "Seat.InvalidQuantity", "Quantity must be between 1 and 500.");
    }

    [Fact]
    public async Task An_unknown_type_is_refused()
    {
        var world = new World();

        var result = await world.PurchaseAsync("Golden", 1);

        world.AssertRefused(result, "Seat.InvalidType", "Unknown seat type.");
    }

    [Fact]
    public async Task A_tenant_without_a_subscription_cannot_buy()
    {
        var world = new World { Subscriptions = new FakeSubscriptionRepo(null) };

        var result = await world.PurchaseAsync("Standard", 1);

        world.AssertRefused(result, "Subscription.NotFound", "Subscription does not exist.");
    }

    [Fact]
    public async Task A_suspended_subscription_cannot_buy()
    {
        var world = new World();
        Assert.True(world.Subscription.SuspendForPolicyViolation("review", Guid.Empty, DateTime.UtcNow).IsSuccess);

        var result = await world.PurchaseAsync("Standard", 1);

        world.AssertRefused(
            result,
            "Subscription.CannotPurchaseSeats",
            "Cannot purchase seats while subscription is Suspended."
        );
    }

    [Fact]
    public async Task The_tenant_limit_counts_live_seats_and_the_new_ones()
    {
        var world = new World();
        world.LimitSeatsTo(2);
        await world.PurchaseAsync("Standard", 1);
        world.Bus.Published.Clear();

        var result = await world.PurchaseAsync("Standard", 2);

        Assert.Equal("Seat.MaxSeatsExceeded", result.Error.Code);
        Assert.Equal("Purchasing 2 seat(s) would exceed the tenant's limit of 2.", result.Error.Message);
        Assert.Single(world.Seats.Added);
    }

    [Fact]
    public async Task The_limit_ignores_seats_already_gone()
    {
        var world = new World();
        world.LimitSeatsTo(1);
        await world.PurchaseAsync("Standard", 1);
        Assert.True(world.Seats.Added[0].CancelActive("gone", Guid.Empty, DateTime.UtcNow).IsSuccess);

        var result = await world.PurchaseAsync("Standard", 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Without_a_price_catalog_nothing_is_bought()
    {
        var world = new World { Pricing = new FakeSeatPricingRepository(null) };

        var result = await world.PurchaseAsync("Standard", 1);

        world.AssertRefused(result, "SeatPricing.NotFound", "Seat pricing catalog does not exist.");
    }

    [Fact]
    public async Task Each_seat_is_charged_the_prorated_price_with_its_own_key()
    {
        var world = new World();

        var result = await world.PurchaseAsync("standard", 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(world.Seats.Added.Select(seat => seat.Id), result.Value);
        var expected = ProrationCalculator
            .InitialPeriod(
                Money.Create(15m, "USD").Value,
                world.Subscription.CurrentPeriodStartUtc,
                world.Subscription.CurrentPeriodEndUtc,
                world.Seats.Added[0].CurrentPeriodStartUtc!.Value
            )
            .Value;
        var charges = world.Bus.Published.OfType<SeatRenewalDueIntegrationEvent>().ToList();
        Assert.Equal(2, charges.Count);
        foreach (var (seat, charge) in world.Seats.Added.Zip(charges))
        {
            Assert.Equal(SeatType.Standard, seat.Type);
            Assert.Equal(SeatStatus.Active, seat.Status);
            Assert.Equal(world.Subscription.PlanId, seat.SourceReferenceId);
            Assert.Equal(15m, seat.UnitPrice.Amount);
            Assert.Equal(world.Subscription.CurrentPeriodEndUtc, seat.CurrentPeriodEndUtc);
            Assert.Equal(seat.Id, charge.SeatId);
            Assert.Equal(seat.TenantId, charge.TenantId);
            Assert.Equal(seat.CurrentPeriodStartUtc, charge.PeriodStartUtc);
            Assert.Equal(seat.CurrentPeriodEndUtc, charge.PeriodEndUtc);
            Assert.Equal(
                IdempotencyKeyFactory.SeatInitialCharge(seat.Id, seat.CurrentPeriodStartUtc!.Value),
                charge.IdempotencyKey
            );
            Assert.Equal((long)Math.Round(expected.Amount * 100m, MidpointRounding.AwayFromZero), charge.AmountCents);
            Assert.Equal("USD", charge.Currency);
            Assert.Equal(world.Correlation.CorrelationId, charge.CorrelationId);
        }
        Assert.IsType<RecalculateEntitlementsCommand>(world.Bus.Published[^1]);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("Seats.Purchased", entry.Action);
        Assert.Equal(nameof(SubscriptionSeat), entry.AggregateType);
        Assert.Equal(world.Seats.Added[0].Id, entry.AggregateId);
        Assert.Contains($"\"BilledCents\":{charges.Sum(charge => charge.AmountCents)}", entry.AfterPayload);
        Assert.Contains("\"Method\":\"OffSession\"", entry.AfterPayload);
        Assert.Equal(("Standard", charges.Sum(charge => charge.AmountCents)), Assert.Single(world.Metrics.SeatsBilled));
    }

    private sealed class World
    {
        public World()
        {
            var now = DateTime.UtcNow;
            var plan = SubscriptionPlan
                .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, now)
                .Value;
            var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
            PlanTestData.EnsurePublishableFeature(version);
            PlanTestData.EnsurePublishablePrice(version);
            plan.AddVersion(version, Guid.Empty, now);
            Assert.True(plan.PublishVersion(version.Id, now, Guid.Empty, now).IsSuccess);
            Subscription = TenantSubscription
                .ActivateImmediately(
                    Guid.NewGuid(),
                    plan,
                    version,
                    BillingCycle.Monthly,
                    now.AddDays(-10),
                    now.AddDays(20),
                    Guid.Empty,
                    now
                )
                .Value;
            Subscriptions = new FakeSubscriptionRepo(Subscription);
            var pricing = SeatPricing.Seed(Guid.NewGuid(), now);
            pricing.AddPriceTier(
                SeatPriceTier
                    .Create(pricing.Id, SeatType.Standard, BillingCycle.Monthly, Money.Create(15m, "USD").Value)
                    .Value
            );
            Pricing = new FakeSeatPricingRepository(pricing);
        }

        public TenantSubscription Subscription { get; }
        public FakeSubscriptionRepo Subscriptions { get; init; }
        public FakeSeatPricingRepository Pricing { get; init; }
        public CapturingSeatRepo Seats { get; } = new();
        public CapturingMessageBus Bus { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeCorrelationContext Correlation { get; } = new();
        public FakeSubscriptionAuditLogWriter Audit { get; } = new();
        public FakeSubscriptionMetrics Metrics { get; } = new();
        public SubscriptionTenantSettings? Settings { get; private set; }

        public void LimitSeatsTo(int max)
        {
            Settings = SubscriptionTenantSettings.Default(Subscription.TenantId, Guid.Empty, DateTime.UtcNow).Value;
            Assert.True(
                Settings
                    .ApplyPatch(new SubscriptionSettingsPatch(MaxSeatsAllowed: max), Guid.Empty, DateTime.UtcNow)
                    .IsSuccess
            );
        }

        public void AssertRefused(Result<IReadOnlyList<Guid>> result, string code, string message)
        {
            Assert.Equal(code, result.Error.Code);
            Assert.Equal(message, result.Error.Message);
            Assert.Empty(Seats.Added);
            Assert.Empty(Bus.Published);
            Assert.Equal(0, UnitOfWork.SaveChangesCallCount);
        }

        public Task<Result<IReadOnlyList<Guid>>> PurchaseAsync(string seatType, int quantity) =>
            PurchaseSeatsHandler.Handle(
                new PurchaseSeatsCommand(Subscription.TenantId, seatType, quantity, true, Guid.NewGuid()),
                Subscriptions,
                Seats,
                new FixedSettings(Settings),
                Pricing,
                UnitOfWork,
                Bus,
                Correlation,
                Audit,
                Metrics,
                NullLogger<SubscriptionSeat>.Instance,
                CancellationToken.None
            );
    }

    private sealed class FixedSettings(SubscriptionTenantSettings? settings) : ISubscriptionTenantSettingsRepository
    {
        public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(settings);

        public Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
