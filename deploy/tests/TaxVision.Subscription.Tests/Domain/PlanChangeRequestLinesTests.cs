using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// El cambio a anual cobra el plan y cada extra en un solo pago. La solicitud guarda ese desglose para aplicar al
/// pagar exactamente lo que se cobró y para que el recibo lo detalle. Mientras espera su pago no se compran extras.
/// </summary>
public sealed class PlanChangeRequestLinesTests
{
    [Fact]
    public void The_request_keeps_the_breakdown_and_charges_its_total()
    {
        var (subscription, plan, version) = MonthlySubscription();
        var addOnId = Guid.NewGuid();
        PlanChangeLine[] lines =
        [
            PlanChangeLine.ForPlan("enterprise", "Enterprise — yearly", 99_900),
            PlanChangeLine.ForAddOn(addOnId, "module-addon-email", "Email — yearly", 1, 29_000),
            PlanChangeLine.ForSeats("Standard", "Extra seats — yearly", 2, 15_000),
        ];

        var result = subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            chargeAmountCents: 158_900,
            "USD",
            "plan-change-token-lines",
            Guid.Empty,
            DateTime.UtcNow,
            lines: lines
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var request = Assert.Single(subscription.PlanChangeRequests);
        Assert.Equal(158_900, request.ChargeAmountCents);
        Assert.Equal(3, request.Lines.Count);
        Assert.Equal(30_000, request.Lines.Single(l => l.Kind == PlanChangeLineKind.Seats).AmountCents);
        Assert.Equal(addOnId, request.Lines.Single(l => l.Kind == PlanChangeLineKind.AddOn).ReferenceId);
    }

    [Fact]
    public void A_breakdown_that_does_not_add_up_to_the_charge_is_rejected()
    {
        var (subscription, plan, version) = MonthlySubscription();

        var result = subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            chargeAmountCents: 120_000,
            "USD",
            "plan-change-token-mismatch",
            Guid.Empty,
            DateTime.UtcNow,
            lines: [PlanChangeLine.ForPlan("enterprise", "Enterprise — yearly", 99_900)]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PlanChangeRequest.LinesMismatch", result.Error.Code);
        Assert.Empty(subscription.PlanChangeRequests);
    }

    // Lo comprado entre la vista previa y el pago quedaría fuera de lo cobrado.
    [Fact]
    public void No_add_on_or_seat_can_be_bought_while_the_change_awaits_its_payment()
    {
        var (subscription, plan, version) = MonthlySubscription();
        subscription.RequestUpgrade(
            plan,
            version,
            BillingCycle.Yearly,
            99_900,
            "USD",
            "plan-change-token-wait",
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.False(subscription.AcceptsPurchases);
        Assert.True(subscription.RecordPurchase(Guid.Empty, DateTime.UtcNow).IsFailure);
    }

    private static (
        TenantSubscription Subscription,
        SubscriptionPlan Plan,
        SubscriptionPlanVersion Version
    ) MonthlySubscription()
    {
        var plan = SubscriptionPlan
            .Create(
                PlanCode.Create("enterprise").Value,
                "Enterprise",
                "plan",
                PlanTier.Starter,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        var now = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                now,
                now.AddDays(30),
                Guid.Empty,
                now
            )
            .Value;
        return (subscription, plan, version);
    }
}
