using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>SUB-11: caracteriza los rechazos y el cargo exacto de la compra de add-ons antes de mover las reglas.</summary>
public sealed class PurchaseAddOnHandlerPathsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task The_quantity_must_be_positive()
    {
        var world = new World();

        var result = await world.PurchaseAsync(quantity: 0);

        world.AssertRefused(result, "AddOn.InvalidQuantity", "Quantity must be at least 1.");
    }

    [Fact]
    public async Task A_tenant_without_a_subscription_cannot_buy()
    {
        var world = new World { Subscriptions = new FakeSubscriptionRepo(null) };

        var result = await world.PurchaseAsync();

        world.AssertRefused(result, "Subscription.NotFound", "Subscription does not exist.");
    }

    [Fact]
    public async Task A_suspended_subscription_cannot_buy()
    {
        var world = new World();
        Assert.True(world.Subscription.SuspendForPolicyViolation("review", Guid.Empty, DateTime.UtcNow).IsSuccess);

        var result = await world.PurchaseAsync();

        world.AssertRefused(
            result,
            "Subscription.CannotPurchaseAddOns",
            "Cannot purchase add-ons while subscription is Suspended."
        );
    }

    [Fact]
    public async Task A_tenant_that_disallows_add_ons_cannot_buy()
    {
        var settings = SubscriptionTenantSettings.Default(TenantId, Guid.Empty, DateTime.UtcNow).Value;
        Assert.True(
            settings
                .ApplyPatch(new SubscriptionSettingsPatch(AllowAddons: false), Guid.Empty, DateTime.UtcNow)
                .IsSuccess
        );
        var world = new World { Settings = settings };

        var result = await world.PurchaseAsync();

        world.AssertRefused(result, "AddOn.NotAllowed", "This tenant does not allow add-on purchases.");
    }

    [Fact]
    public async Task An_unknown_code_does_not_exist()
    {
        var world = new World();

        var result = await world.PurchaseAsync(code: "missing");

        world.AssertRefused(result, "AddOnDefinition.NotFound", "Add-on does not exist.");
    }

    [Fact]
    public async Task The_add_on_is_charged_the_prorated_price_after_announcing_it()
    {
        var world = new World();

        var result = await world.PurchaseAsync(quantity: 1);

        Assert.True(result.IsSuccess);
        var addOn = Assert.Single(world.AddOns.Added);
        Assert.Equal(result.Value, addOn.Id);
        Assert.Equal(TenantId, addOn.TenantId);
        Assert.Equal(BillingCycle.Monthly, addOn.BillingCycle);
        Assert.Equal(29m, addOn.UnitPrice.Amount);
        Assert.Equal(world.Subscription.CurrentPeriodEndUtc, addOn.CurrentPeriodEndUtc);
        Assert.Equal(1, world.UnitOfWork.SaveChangesCallCount);

        var activated = Assert.IsType<AddOnActivatedIntegrationEvent>(world.Bus.Published[0]);
        Assert.Equal(addOn.Id, activated.TenantAddOnId);
        Assert.Equal(addOn.AddOnCode, activated.AddOnCode);
        Assert.Equal(1, activated.Quantity);
        Assert.Equal(addOn.CurrentPeriodEndUtc, activated.CurrentPeriodEndUtc);

        var charge = Assert.IsType<AddOnRenewalDueIntegrationEvent>(world.Bus.Published[1]);
        var expected = ProrationCalculator
            .InitialPeriod(
                addOn.UnitPrice,
                world.Subscription.CurrentPeriodStartUtc,
                world.Subscription.CurrentPeriodEndUtc,
                addOn.CurrentPeriodStartUtc
            )
            .Value;
        Assert.Equal(addOn.Id, charge.TenantAddOnId);
        Assert.Equal(TenantId, charge.TenantId);
        Assert.Equal(addOn.AddOnCode, charge.AddOnCode);
        Assert.Equal(addOn.CurrentPeriodStartUtc, charge.PeriodStartUtc);
        Assert.Equal(addOn.CurrentPeriodEndUtc, charge.PeriodEndUtc);
        Assert.Equal(
            IdempotencyKeyFactory.AddOnInitialCharge(addOn.Id, addOn.CurrentPeriodStartUtc),
            charge.IdempotencyKey
        );
        Assert.Equal((long)Math.Round(expected.Amount * 100m, MidpointRounding.AwayFromZero), charge.AmountCents);
        Assert.Equal("USD", charge.Currency);
        Assert.Equal(world.Correlation.CorrelationId, charge.CorrelationId);

        Assert.IsType<RecalculateEntitlementsCommand>(world.Bus.Published[2]);
        Assert.Equal(addOn.AddOnCode, Assert.Single(world.Metrics.Purchased));
        Assert.Equal((addOn.AddOnCode, charge.AmountCents), Assert.Single(world.Metrics.Billed));
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("AddOn.Purchased", entry.Action);
        Assert.Equal("TenantAddOn", entry.AggregateType);
        Assert.Equal(addOn.Id, entry.AggregateId);
    }

    private sealed class World
    {
        public World()
        {
            var plan = AddOnTestCatalog.PlanWith("starter", modules: ["documents"]);
            Definition = AddOnTestCatalog.QuotaAddOn("quota-addon");
            Subscription = AddOnTestCatalog.ActiveSubscription(TenantId, plan);
            Subscriptions = new FakeSubscriptionRepo(Subscription);
            Plans = new FakePlanRepo(plan);
        }

        public AddOnDefinition Definition { get; }
        public TenantSubscription Subscription { get; }
        public FakeSubscriptionRepo Subscriptions { get; init; }
        public FakePlanRepo Plans { get; }
        public SubscriptionTenantSettings? Settings { get; init; }
        public FakeTenantAddOnRepo AddOns { get; } = new();
        public CapturingMessageBus Bus { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeCorrelationContext Correlation { get; } = new();
        public FakeSubscriptionAuditLogWriter Audit { get; } = new();
        public FakeSubscriptionMetrics Metrics { get; } = new();

        public void AssertRefused(Result<Guid> result, string code, string message)
        {
            Assert.Equal(code, result.Error.Code);
            Assert.Equal(message, result.Error.Message);
            Assert.Empty(AddOns.Added);
            Assert.Empty(Bus.Published);
            Assert.Equal(0, UnitOfWork.SaveChangesCallCount);
        }

        public Task<Result<Guid>> PurchaseAsync(string? code = null, int quantity = 1) =>
            PurchaseAddOnHandler.Handle(
                new PurchaseAddOnCommand(TenantId, code ?? Definition.Code.Value, quantity, true, Guid.NewGuid()),
                new TaxVision.Subscription.Application.AddOns.AddOnPurchaseEligibility(
                    Subscriptions,
                    Plans,
                    new FakeModuleRepo(),
                    new FakeAddOnDefinitionRepository(Definition),
                    new FixedSettings(Settings),
                    AddOns
                ),
                AddOns,
                UnitOfWork,
                Bus,
                Correlation,
                Audit,
                Metrics,
                NullLogger<TenantAddOn>.Instance,
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
