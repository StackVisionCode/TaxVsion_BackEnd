using TaxVision.Subscription.Application.Subscriptions.Renewals;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// D-D: en el aniversario el plan renueva junto con los asientos y add-ons que vencen ese día, en un solo cobro con
/// una línea por cosa. El resultado del cobro se aplica a cada uno; el fallo no manda un correo por ítem.
/// </summary>
public sealed class RenewalBundleTests
{
    private const string PlanKey = "subscription-renewal-x-20261216";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime PlanEnd = Now.AddHours(-1);

    [Fact]
    public void Only_active_extras_ending_with_the_plan_go_in_the_bundle()
    {
        var plan = Plan();
        var aligned = Seat(PlanEnd);
        var otherDate = Seat(PlanEnd.AddDays(5));
        var resizing = AddOn(quantity: 1);

        Assert.True(RenewalBundle.Includes(plan, aligned));
        Assert.False(RenewalBundle.Includes(plan, otherDate));
        Assert.True(RenewalBundle.Includes(plan, resizing));
    }

    [Fact]
    public void A_plan_that_ends_for_good_bundles_nothing()
    {
        var plan = Plan();
        plan.ScheduleCancellation("too expensive", Guid.Empty, Now);

        Assert.False(RenewalBundle.Includes(plan, Seat(PlanEnd)));
    }

    [Fact]
    public void Beginning_the_bundle_schedules_each_extra_until_the_next_plan_end_with_one_line_each()
    {
        var plan = Plan();
        var seat = Seat(PlanEnd);
        var storage = AddOn(quantity: 2);

        var extras = RenewalBundle.Begin(
            plan,
            PlanKey,
            [seat, Seat(PlanEnd.AddDays(5))],
            [storage],
            new Dictionary<string, string> { ["storage.extra_100gb"] = "Extra storage" },
            Now
        );

        Assert.Equal(2, extras.Lines.Count);
        var seatLine = extras.Lines.Single(line => line.TargetKind == "Seat");
        Assert.Equal((seat.Id, 1, 1_500L), (seatLine.TargetId!.Value, seatLine.Quantity, seatLine.UnitAmountCents));
        // P-1: el add-on cobra precio × cantidad.
        var addOnLine = extras.Lines.Single(line => line.TargetKind == "AddOn");
        Assert.Equal(
            ("Extra storage", 2, 500L),
            (addOnLine.Description, addOnLine.Quantity, addOnLine.UnitAmountCents)
        );
        Assert.Equal(2_500, extras.NetCents);
        var renewal = Assert.Single(seat.Renewals);
        Assert.Equal(RenewalBundle.SeatKey(PlanKey, seat.Id), renewal.IdempotencyKey);
        Assert.Equal(BillingCycle.Monthly.CalculateNext(plan.CurrentPeriodEndUtc), renewal.PeriodEndUtc);
    }

    [Fact]
    public void A_paid_bundle_renews_each_extra()
    {
        var plan = Plan();
        var seat = Seat(PlanEnd);
        var storage = AddOn(quantity: 1);
        RenewalBundle.Begin(plan, PlanKey, [seat], [storage], new Dictionary<string, string>(), Now);

        RenewalBundle.Complete(PlanKey, [seat], [storage], "pi_1", Now);

        Assert.Equal(BillingCycle.Monthly.CalculateNext(PlanEnd), seat.CurrentPeriodEndUtc);
        Assert.Equal(BillingCycle.Monthly.CalculateNext(PlanEnd), storage.CurrentPeriodEndUtc);
    }

    [Fact]
    public void A_bundle_that_runs_out_of_retries_suspends_each_extra()
    {
        var plan = Plan();
        var seat = Seat(PlanEnd);
        var storage = AddOn(quantity: 1);
        RenewalBundle.Begin(plan, PlanKey, [seat], [storage], new Dictionary<string, string>(), Now);

        var failed = RenewalBundle.Fail(
            PlanKey,
            [seat],
            [storage],
            "card_declined",
            "Declined",
            willRetry: false,
            nextRetryAtUtc: null,
            Now
        );

        Assert.Equal(SeatStatus.Suspended, seat.Status);
        Assert.Equal(AddOnStatus.Suspended, storage.Status);
        Assert.Equal([seat.Id], failed.SuspendedSeatIds);
    }

    [Fact]
    public void A_failure_with_retries_left_keeps_the_extras_active()
    {
        var plan = Plan();
        var seat = Seat(PlanEnd);
        RenewalBundle.Begin(plan, PlanKey, [seat], [], new Dictionary<string, string>(), Now);

        RenewalBundle.Fail(PlanKey, [seat], [], "card_declined", "Declined", true, Now.AddHours(6), Now);

        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.Equal(RenewalStatus.RetryScheduled, Assert.Single(seat.Renewals).Status);
    }

    private static TenantSubscription Plan() => TestSubscriptions.Active(TenantId, PlanEnd.AddMonths(-1), PlanEnd);

    private static SubscriptionSeat Seat(DateTime periodEndUtc)
    {
        var seat = SubscriptionSeat
            .Purchase(
                TenantId,
                SeatType.Standard,
                SeatSourceType.Manual,
                null,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                Now
            )
            .Value;
        seat.Activate(periodEndUtc.AddMonths(-1), periodEndUtc, Guid.Empty, Now);
        return seat;
    }

    private static TenantAddOn AddOn(int quantity)
    {
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("storage.extra_100gb").Value,
                "Extra storage",
                "desc",
                "storage",
                true,
                [BillingCycle.Monthly],
                Guid.Empty,
                Now
            )
            .Value;
        var addOn = TenantAddOn
            .Purchase(
                TenantId,
                definition,
                quantity,
                Money.Create(5m, "USD").Value,
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                PlanEnd.AddMonths(-1)
            )
            .Value;
        addOn.CoTermTo(PlanEnd, Guid.Empty, Now);
        return addOn;
    }
}
