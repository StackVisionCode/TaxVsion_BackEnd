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
/// "Pay what's due": lo vencido se paga junto, con un recibo. Se pueden desmarcar asientos y add-ons, pero el plan
/// vencido va siempre (D-B). Con el plan vencido todo arranca el día del pago y termina junto con el plan (P-2);
/// con el plan al día, lo pagado termina con el plan y cobra ese tramo.
/// </summary>
public sealed class DuePaymentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["storage.extra_100gb"] = "Extra storage",
    };

    [Fact]
    public void With_the_plan_up_to_date_a_suspended_seat_pays_up_to_the_plan_end()
    {
        var plan = TestSubscriptions.Active(TenantId, Now.AddDays(-20), Now.AddDays(10));
        var seat = SuspendedSeat();

        var quote = DuePayment.Quote(plan, PlanPrice, "Pro", [seat], [], Names, selection: null, Now).Value;

        Assert.False(quote.IncludesPlan);
        Assert.False(quote.PlanRequired);
        var item = Assert.Single(quote.Items);
        Assert.Equal((DuePayment.SeatKind, seat.Id), (item.Kind, item.TargetId));
        Assert.InRange(item.NetCents, 450, 550);
        Assert.Equal(item.NetCents, quote.TotalNetCents);
    }

    [Fact]
    public void An_overdue_plan_goes_first_and_everything_starts_on_the_payment_day()
    {
        var plan = SuspendedPlan();
        var seat = SuspendedSeat();

        var quote = DuePayment.Quote(plan, PlanPrice, "Pro", [seat], [], Names, selection: null, Now).Value;

        Assert.True(quote.IncludesPlan);
        Assert.True(quote.PlanRequired);
        Assert.Equal([DuePayment.PlanKind, DuePayment.SeatKind], quote.Items.Select(item => item.Kind));
        Assert.Equal(2_900 + 1_500, quote.TotalNetCents);
    }

    [Fact]
    public void The_overdue_plan_cannot_be_left_out()
    {
        var plan = SuspendedPlan();
        var seat = SuspendedSeat();

        var quote = DuePayment.Quote(plan, PlanPrice, "Pro", [seat], [], Names, new DueSelection([], []), Now).Value;

        Assert.True(quote.IncludesPlan);
        Assert.Equal(DuePayment.PlanKind, Assert.Single(quote.Items).Kind);
    }

    [Fact]
    public void A_deselected_seat_is_not_charged()
    {
        var plan = TestSubscriptions.Active(TenantId, Now.AddDays(-20), Now.AddDays(10));
        var kept = SuspendedSeat();
        var dropped = SuspendedSeat();

        var quote = DuePayment
            .Quote(plan, PlanPrice, "Pro", [kept, dropped], [], Names, new DueSelection([kept.Id], []), Now)
            .Value;

        Assert.Equal(kept.Id, Assert.Single(quote.Items).TargetId);
    }

    [Fact]
    public void An_item_that_is_not_due_is_rejected()
    {
        var plan = TestSubscriptions.Active(TenantId, Now.AddDays(-20), Now.AddDays(10));

        var quote = DuePayment.Quote(
            plan,
            PlanPrice,
            "Pro",
            [SuspendedSeat()],
            [],
            Names,
            new DueSelection([Guid.NewGuid()], []),
            Now
        );

        Assert.Equal("Subscription.Due.UnknownItem", quote.Error.Code);
    }

    [Fact]
    public void Nothing_due_is_an_error()
    {
        var plan = TestSubscriptions.Active(TenantId, Now.AddDays(-20), Now.AddDays(10));

        var quote = DuePayment.Quote(plan, PlanPrice, "Pro", [], [], Names, selection: null, Now);

        Assert.Equal("Subscription.Due.NothingDue", quote.Error.Code);
    }

    [Fact]
    public void A_suspended_add_on_pays_price_times_quantity()
    {
        var plan = SuspendedPlan();
        var storage = SuspendedAddOn(quantity: 2);

        var quote = DuePayment.Quote(plan, PlanPrice, "Pro", [], [storage], Names, selection: null, Now).Value;

        var item = quote.Items.Single(item => item.Kind == DuePayment.AddOnKind);
        Assert.Equal(1_000, item.NetCents);
        Assert.Contains("Extra storage", item.Description);
    }

    [Fact]
    public void Paying_reactivates_each_item_until_the_plan_end_and_stops_its_retries()
    {
        var plan = TestSubscriptions.Active(TenantId, Now.AddDays(-20), Now.AddDays(10));
        var seat = SuspendedSeat();
        var storage = SuspendedAddOn(quantity: 1);
        var quote = DuePayment.Quote(plan, PlanPrice, "Pro", [seat], [storage], Names, null, Now).Value;
        var intent = DuePayment.Intent(TenantId, quote, BillingCycle.Monthly, Guid.Empty, Now).Value;

        var superseded = DuePaymentProvisioning.Apply(intent, plan, [seat], [storage], Now);

        Assert.Equal(SeatStatus.Active, seat.Status);
        Assert.Equal(plan.CurrentPeriodEndUtc, seat.CurrentPeriodEndUtc);
        Assert.Equal(AddOnStatus.Active, storage.Status);
        Assert.Equal(plan.CurrentPeriodEndUtc, storage.CurrentPeriodEndUtc);
        Assert.Contains("seat-renewal-x", superseded);
        Assert.DoesNotContain(seat.Renewals, renewal => renewal.Status == RenewalStatus.RetryScheduled);
    }

    private static readonly (long AmountCents, string Currency) PlanPrice = (2_900, "USD");

    private static TenantSubscription SuspendedPlan()
    {
        var plan = TestSubscriptions.Active(TenantId, Now.AddMonths(-2), Now.AddMonths(-1));
        plan.BeginRenewal("plan-key", Guid.Empty, Now);
        plan.FailRenewal(
            plan.FindRenewalByIdempotencyKey("plan-key")!.Id,
            "card_declined",
            "Declined",
            willRetry: false,
            nextRetryAtUtc: null,
            Guid.Empty,
            Now
        );
        plan.SuspendAfterRetriesExhausted(Guid.Empty, Now);
        Assert.Equal(SubscriptionStatus.Suspended, plan.Status);
        return plan;
    }

    private static SubscriptionSeat SuspendedSeat()
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
        seat.Activate(Now.AddDays(-40), Now.AddDays(-10), Guid.Empty, Now);
        seat.BeginRenewal("seat-renewal-x", Guid.Empty, Now);
        seat.FailRenewal(seat.Renewals.First().Id, "card_declined", "Declined", true, Now.AddHours(6), Guid.Empty, Now);
        seat.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, Now);
        seat.SuspendAfterRetriesExhausted(Guid.Empty, Now);
        return seat;
    }

    private static TenantAddOn SuspendedAddOn(int quantity)
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
                Now.AddDays(-40)
            )
            .Value;
        addOn.BeginRenewal("addon-renewal-x", Now.AddDays(-5), Guid.Empty, Now);
        addOn.FailRenewal(addOn.Renewals.First().Id, "card_declined", "Declined", false, null, Guid.Empty, Now);
        addOn.SuspendAfterRetriesExhausted(Guid.Empty, Now);
        return addOn;
    }
}
