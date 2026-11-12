using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Pagado el cambio a anual, el plan, cada add-on y los asientos pasan juntos al ciclo nuevo: al precio que se cobró
/// (el del desglose guardado) y con el mismo período que la base.
/// </summary>
public sealed class SubscriptionPlanChangePaymentSucceededConsumerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task The_paid_switch_moves_the_add_ons_and_seats_to_the_new_cycle_and_period()
    {
        var (subscription, plan, version, definition) = World();
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

        subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            143_900,
            "USD",
            "plan-change-token-paid",
            Guid.Empty,
            Now,
            lines:
            [
                PlanChangeLine.ForPlan("enterprise", "Enterprise — yearly", 99_900),
                PlanChangeLine.ForAddOn(addOn.Id, "module-addon-email", "Email — yearly", 1, 29_000),
                PlanChangeLine.ForSeats("Standard", "Extra seats (Standard) — yearly", 1, 15_000),
            ]
        );
        var request = Assert.Single(subscription.PlanChangeRequests);

        await SubscriptionPlanChangePaymentSucceededConsumer.Handle(
            new SubscriptionPlanChangePaymentSucceededIntegrationEvent
            {
                TenantId = subscription.TenantId,
                PlanChangeRequestId = request.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = request.PaymentIdempotencyKey,
                ExternalPaymentReference = "pi_test",
                PaidAtUtc = Now,
                RequestedByUserId = Guid.Empty,
            },
            new FakeSubscriptionRepo(subscription),
            new InMemoryPlanRepo(plan),
            new FakeTenantAddOnRepo([addOn]),
            new FakeAddOnDefinitionRepository(definition),
            new FakeSeatRepo([seat]),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            new FakeSubscriptionMetrics(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Equal(BillingCycle.Yearly, subscription.BillingCycle);
        Assert.Equal((BillingCycle.Yearly, 290m), (addOn.BillingCycle, addOn.UnitPrice.Amount));
        Assert.Equal((BillingCycle.Yearly, 150m), (seat.BillingCycle, seat.UnitPrice.Amount));
        Assert.Equal(subscription.CurrentPeriodEndUtc, addOn.CurrentPeriodEndUtc);
        Assert.Equal(subscription.CurrentPeriodEndUtc, seat.CurrentPeriodEndUtc);
        Assert.Equal(subscription.CurrentPeriodStartUtc, seat.CurrentPeriodStartUtc);
    }

    private static Money Usd(decimal amount) => Money.Create(amount, "USD").Value;

    private static (TenantSubscription, SubscriptionPlan, SubscriptionPlanVersion, AddOnDefinition) World()
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
        return (subscription, plan, version, definition);
    }
}
