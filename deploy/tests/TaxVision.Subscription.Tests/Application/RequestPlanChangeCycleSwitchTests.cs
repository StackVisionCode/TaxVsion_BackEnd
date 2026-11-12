using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Pedir el paso a anual cobra en un solo pago el plan y cada extra, y el pedido de cobro lleva ese desglose y el
/// concepto "Switch to yearly billing" para que el recibo y el historial lo detallen.
/// </summary>
public sealed class RequestPlanChangeCycleSwitchTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task The_charge_request_carries_every_line_and_the_cycle_switch_concept()
    {
        var world = World();
        var bus = new CapturingMessageBus();

        var result = await HandleAsync(world, bus);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var due = Assert.Single(bus.Published.OfType<SubscriptionPlanChangeDueIntegrationEvent>());
        Assert.Equal(99_900 + 29_000 + 15_000, due.TargetPlanPrice);
        Assert.Equal("Switch to yearly billing", due.ConceptDisplayName);
        Assert.Equal(3, due.Lines!.Count);
        Assert.Equal(3, Assert.Single(world.Subscription.PlanChangeRequests).Lines.Count);
    }

    [Fact]
    public async Task A_blocked_switch_charges_nothing_and_says_why()
    {
        var world = World();
        world.Seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, Now);
        var bus = new CapturingMessageBus();

        var result = await HandleAsync(world, bus);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingCycleSwitch.ExtraPastDue.Code, result.Error.Code);
        Assert.Empty(bus.Published.OfType<SubscriptionPlanChangeDueIntegrationEvent>());
        Assert.Empty(world.Subscription.PlanChangeRequests);
    }

    private static Task<BuildingBlocks.Results.Result<PlanChangeRequested>> HandleAsync(
        TestWorld world,
        CapturingMessageBus bus
    )
    {
        var seats = new CapturingSeatRepo();
        seats.Added.Add(world.Seat);
        return RequestPlanChangeHandler.Handle(
            new RequestPlanChangeCommand(
                new ChangePlanCommand(world.Subscription.TenantId, "enterprise", "Yearly", Guid.Empty)
            ),
            new FakeSubscriptionRepo(world.Subscription),
            new InMemoryPlanRepo(world.Plan),
            seats,
            FakeTenantUserCountClient.WithActive(1),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            NullLogger<TenantSubscription>.Instance,
            new FakeTenantAddOnRepo([world.AddOn]),
            new FakeAddOnDefinitionRepository(world.Definition),
            new FakeSeatPricingRepository(world.SeatPricing),
            CancellationToken.None
        );
    }

    private sealed record TestWorld(
        TenantSubscription Subscription,
        SubscriptionPlan Plan,
        AddOnDefinition Definition,
        TenantAddOn AddOn,
        SubscriptionSeat Seat,
        SeatPricing SeatPricing
    );

    private static Money Usd(decimal amount) => Money.Create(amount, "USD").Value;

    private static TestWorld World()
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
                [BillingCycle.Monthly, BillingCycle.Yearly],
                Guid.Empty,
                Now
            )
            .Value;
        definition.AddPriceTier(AddOnPriceTier.Create(definition.Id, BillingCycle.Monthly, 1, null, Usd(29m)).Value);
        definition.AddPriceTier(AddOnPriceTier.Create(definition.Id, BillingCycle.Yearly, 1, null, Usd(290m)).Value);
        definition.Publish(Guid.Empty, Now);
        var addOn = TenantAddOn
            .Purchase(subscription.TenantId, definition, 1, Usd(29m), BillingCycle.Monthly, true, Guid.Empty, Now)
            .Value;

        var seat = SubscriptionSeat
            .Purchase(
                subscription.TenantId,
                SeatType.Standard,
                SeatSourceType.Plan,
                null,
                Usd(15m),
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                Now
            )
            .Value;
        seat.Activate(Now, Now.AddDays(30), Guid.Empty, Now);

        var pricing = SeatPricing.Seed(Guid.NewGuid(), Now);
        pricing.SetTypePrices(SeatType.Standard, Usd(15m), Usd(150m), Guid.Empty, Now);

        return new TestWorld(subscription, plan, definition, addOn, seat, pricing);
    }
}
