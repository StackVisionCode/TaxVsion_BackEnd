using TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;
using TaxVision.Subscription.Application.Catalog.Commands.UpdateModule;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanPrices;
using TaxVision.Subscription.Application.Plans.Commands.UpdatePlanMetadata;
using TaxVision.Subscription.Application.Seats.Commands.SetSeatPrices;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

// D13: cada cambio del catálogo deja una entrada con quién, qué, antes y después. Un pedido que falla, no.
public sealed class CatalogHandlersAuditTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Renaming_a_plan_is_audited_with_before_and_after()
    {
        var plan = Plan("pro");
        var audit = new InMemoryCatalogAuditLog();

        await UpdatePlanMetadataHandler.Handle(
            new UpdatePlanMetadataCommand(plan.Id, "Pro Plus", "Better", Admin),
            new InMemoryPlanRepo(plan),
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            new CapturingMessageBus(),
            CancellationToken.None
        );

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("plan.metadata", entry.Action);
        Assert.Contains("\"Name\":\"pro\"", entry.BeforeJson);
        Assert.Contains("\"Name\":\"Pro Plus\"", entry.AfterJson);
        Assert.Equal(Admin, entry.ActorUserId);
    }

    [Fact]
    public async Task Changing_a_plan_price_is_audited()
    {
        var plan = Plan("pro");
        var audit = new InMemoryCatalogAuditLog();

        await SetPlanPricesHandler.Handle(
            new SetPlanPricesCommand(plan.Id, 59m, 590m, Admin),
            new InMemoryPlanRepo(plan),
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            new CapturingMessageBus(),
            CancellationToken.None
        );

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("plan.prices", entry.Action);
        Assert.Contains("\"Monthly\":59", entry.AfterJson);
    }

    [Fact]
    public async Task Changing_an_add_on_price_is_audited()
    {
        var addOn = AddOnTestCatalog.QuotaAddOn("test.pack");
        var audit = new InMemoryCatalogAuditLog();

        var result = await SetAddOnPricesHandler.Handle(
            new SetAddOnPricesCommand(addOn.Id, 7m, 70m, Admin),
            new FakeAddOnDefinitionRepository(addOn),
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            new CapturingMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("addon.prices", entry.Action);
        Assert.Equal("AddOn", entry.EntityType);
        Assert.Contains("\"Amount\":7", entry.AfterJson);
    }

    [Fact]
    public async Task Changing_a_seat_price_is_audited()
    {
        var pricing = SeatPricing.Seed(Guid.NewGuid(), DateTime.UtcNow);
        pricing.AddPriceTier(
            SeatPriceTier
                .Create(pricing.Id, SeatType.Standard, BillingCycle.Monthly, Money.Create(12m, "USD").Value)
                .Value
        );
        pricing.AddPriceTier(
            SeatPriceTier
                .Create(pricing.Id, SeatType.Standard, BillingCycle.Yearly, Money.Create(120m, "USD").Value)
                .Value
        );
        var audit = new InMemoryCatalogAuditLog();

        await SetSeatPricesHandler.Handle(
            new SetSeatPricesCommand("Standard", 15m, 150m, Admin),
            new FakeSeatPricingRepository(pricing),
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("seat.prices", entry.Action);
        Assert.Contains("\"Amount\":12", entry.BeforeJson);
        Assert.Contains("\"Amount\":15", entry.AfterJson);
    }

    [Fact]
    public async Task Changing_a_module_is_audited()
    {
        var module = Module.Create("wallet", "Wallet", "Wallet module").Value;
        var audit = new InMemoryCatalogAuditLog();

        await UpdateModuleHandler.Handle(
            new UpdateModuleCommand("wallet", "Commercial", 15m, 150m, Admin),
            new FakeModuleRepo(module),
            new NoOpModuleEditSerializer(),
            new FakeUnitOfWork(),
            audit,
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("module.updated", entry.Action);
        Assert.Contains("\"Category\":\"Core\"", entry.BeforeJson);
        Assert.Contains("\"Category\":\"Commercial\"", entry.AfterJson);
    }

    [Fact]
    public async Task A_rejected_change_leaves_no_audit()
    {
        var plan = Plan("pro");
        var audit = new InMemoryCatalogAuditLog();

        await UpdatePlanMetadataHandler.Handle(
            new UpdatePlanMetadataCommand(plan.Id, "", "Better", Admin),
            new InMemoryPlanRepo(plan),
            new FakeUnitOfWork(),
            audit,
            new FakeCorrelationContext(),
            new CapturingMessageBus(),
            CancellationToken.None
        );

        Assert.Empty(audit.Entries);
    }

    private static SubscriptionPlan Plan(string code)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Pro, Admin, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        plan.AddVersion(version, Admin, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Admin, nowUtc);
        return plan;
    }
}
