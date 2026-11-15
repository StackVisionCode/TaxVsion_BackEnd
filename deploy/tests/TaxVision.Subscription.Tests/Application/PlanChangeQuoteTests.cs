using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Lo que cobra pasar a anual: el plan anual más el precio anual vigente de cada add-on y asiento extra, completo y
/// en un solo pago. La vista previa y el pedido usan el mismo cálculo; el ahorro se compara con lo que paga hoy.
/// </summary>
public sealed class PlanChangeQuoteTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Switching_to_yearly_charges_the_plan_and_every_extra_in_one_breakdown()
    {
        var world = World();

        var quote = await QuoteAsync(world, "Yearly");

        Assert.Null(quote.Blocked);
        Assert.Collection(
            quote.Lines,
            plan =>
                Assert.Equal((PlanChangeLineKind.Plan, 1, 99_900L), (plan.Kind, plan.Quantity, plan.UnitAmountCents)),
            addOn =>
                Assert.Equal(
                    (PlanChangeLineKind.AddOn, 1, 29_000L),
                    (addOn.Kind, addOn.Quantity, addOn.UnitAmountCents)
                ),
            seats =>
                Assert.Equal(
                    (PlanChangeLineKind.Seats, 2, 15_000L),
                    (seats.Kind, seats.Quantity, seats.UnitAmountCents)
                )
        );
        Assert.Equal(158_900, quote.TotalCents);
        // Hoy paga 99 + 29 + 15 + 10 (asiento con precio viejo) = 153 al mes → 1 836 al año.
        Assert.Equal(183_600 - 158_900, quote.SavingsCents);
    }

    // E2: un add-on sin precio anual o retirado bloquea el cambio y se nombra.
    [Fact]
    public async Task An_add_on_without_a_yearly_price_blocks_the_switch_and_is_named()
    {
        var world = World(addOnHasYearlyPrice: false);

        var quote = await QuoteAsync(world, "Yearly");

        Assert.NotNull(quote.Blocked);
        Assert.Equal(PlanChangeQuote.AddOnUnavailableCode, quote.Blocked!.Code);
        Assert.Contains("Email", quote.Blocked.Message);
    }

    // E3.
    [Fact]
    public async Task A_past_due_seat_blocks_the_switch()
    {
        var world = World();
        world.Seats[0].MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, Now);

        var quote = await QuoteAsync(world, "Yearly");

        Assert.Equal(BillingCycleSwitch.ExtraPastDue.Code, quote.Blocked?.Code);
    }

    [Fact]
    public async Task A_change_that_keeps_the_cycle_charges_only_the_plan()
    {
        var world = World();

        var quote = await QuoteAsync(world, "Monthly");

        Assert.Empty(quote.Lines);
        Assert.Equal(0, quote.TotalCents);
    }

    private static async Task<PlanChangeQuote> QuoteAsync(TestWorld world, string cycle)
    {
        var resolved = await PlanChangeResolver.ResolveAsync(
            world.Subscription,
            "enterprise",
            cycle,
            new InMemoryPlanRepo(world.Plan),
            CancellationToken.None
        );
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : null);
        var seats = new CapturingSeatRepo();
        seats.Added.AddRange(world.Seats);
        return await PlanChangeQuote.BuildAsync(
            world.Subscription,
            resolved.Value,
            new FakeTenantAddOnRepo(world.AddOns),
            new FakeAddOnDefinitionRepository(world.Definition),
            seats,
            new FakeSeatPricingRepository(world.SeatPricing),
            CancellationToken.None
        );
    }

    private sealed record TestWorld(
        TenantSubscription Subscription,
        SubscriptionPlan Plan,
        AddOnDefinition Definition,
        List<TenantAddOn> AddOns,
        List<SubscriptionSeat> Seats,
        SeatPricing SeatPricing
    );

    private static Money Usd(decimal amount) => Money.Create(amount, "USD").Value;

    private static TestWorld World(bool addOnHasYearlyPrice = true)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("enterprise").Value, "Enterprise", "plan", PlanTier.Starter, Guid.Empty, Now)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        version.AddPriceTier(PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Usd(99m)).Value);
        version.AddPriceTier(PlanPriceTier.Create(version.Id, BillingCycle.Yearly, 1, null, Usd(999m)).Value);
        PlanTestData.EnsurePublishableFeature(version);
        plan.AddVersion(version, Guid.Empty, Now);
        plan.PublishVersion(version.Id, Now, Guid.Empty, Now);
        plan.PutOnSale(Guid.Empty, Now);

        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                Now,
                Now.AddDays(30),
                Guid.Empty,
                Now
            )
            .Value;

        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("module-addon-email").Value,
                "Email",
                "desc",
                "module",
                allowMultipleInstances: false,
                addOnHasYearlyPrice ? [BillingCycle.Monthly, BillingCycle.Yearly] : [BillingCycle.Monthly],
                Guid.Empty,
                Now
            )
            .Value;
        definition.AddPriceTier(AddOnPriceTier.Create(definition.Id, BillingCycle.Monthly, 1, null, Usd(29m)).Value);
        if (addOnHasYearlyPrice)
            definition.AddPriceTier(
                AddOnPriceTier.Create(definition.Id, BillingCycle.Yearly, 1, null, Usd(290m)).Value
            );
        definition.Publish(Guid.Empty, Now);

        var addOn = TenantAddOn
            .Purchase(subscription.TenantId, definition, 1, Usd(29m), BillingCycle.Monthly, true, Guid.Empty, Now)
            .Value;

        var seats = new List<SubscriptionSeat>
        {
            ActiveSeat(subscription.TenantId, 15m),
            ActiveSeat(subscription.TenantId, 10m),
        };

        var pricing = SeatPricing.Seed(Guid.NewGuid(), Now);
        pricing.SetTypePrices(SeatType.Standard, Usd(15m), Usd(150m), Guid.Empty, Now);

        return new TestWorld(subscription, plan, definition, [addOn], seats, pricing);
    }

    private static SubscriptionSeat ActiveSeat(Guid tenantId, decimal monthlyPrice)
    {
        var seat = SubscriptionSeat
            .Purchase(
                tenantId,
                SeatType.Standard,
                SeatSourceType.Plan,
                null,
                Usd(monthlyPrice),
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                Now
            )
            .Value;
        seat.Activate(Now, Now.AddDays(30), Guid.Empty, Now);
        return seat;
    }
}
