using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// El descuento vigente vive en la suscripción, con una ventana de fechas como en Stripe: todo cobro cuya fecha cae
/// dentro de la ventana lleva el descuento. Uno solo a la vez; termina por fecha, por el admin o si el plan sale de su
/// alcance.
/// </summary>
public sealed class SubscriptionDiscountTests
{
    private static readonly DateTime Jan1 = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Admin = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [Fact]
    public void Charge_inside_the_window_gets_the_discount()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Months, months: 3));

        Assert.Equal(2_000, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddMonths(1)));
        Assert.Equal(2_000, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddMonths(2)));
    }

    [Fact]
    public void Charge_after_the_window_pays_full_price()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Months, months: 3));

        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddMonths(3)));
    }

    [Fact]
    public void Forever_never_ends_by_date()
    {
        var subscription = WithDiscount(Percent(1_000, DiscountDuration.Forever));

        Assert.Equal(1_000, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddYears(5)));
    }

    [Fact]
    public void Once_only_covers_the_initial_charge()
    {
        var subscription = WithDiscount(Percent(3_000, DiscountDuration.Once));

        Assert.Equal(3_000, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1));
        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddMonths(1)));
    }

    [Fact]
    public void Fixed_amount_never_exceeds_the_charge()
    {
        var subscription = WithDiscount(Fixed(5_000, "USD", DiscountDuration.Forever));

        Assert.Equal(3_000, subscription.DiscountFor(DiscountChargeKind.Base, 3_000, "USD", Jan1.AddMonths(1)));
        // Otra moneda: no se convierte, el cobro va sin descuento.
        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 3_000, "EUR", Jan1.AddMonths(1)));
    }

    [Fact]
    public void Only_one_active_discount()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Forever));

        var second = subscription.GrantDiscount(Percent(1_000, DiscountDuration.Forever), replace: false, Admin, Jan1);
        Assert.Equal("Subscription.Discount.AlreadyActive", second.Error.Code);

        var replaced = subscription.GrantDiscount(Percent(1_000, DiscountDuration.Forever), replace: true, Admin, Jan1);
        Assert.True(replaced.IsSuccess);
        Assert.Equal(1_000, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddMonths(1)));
        Assert.Single(subscription.Discounts, discount => discount.EndedAtUtc is null);
    }

    [Fact]
    public void Plan_change_outside_scope_ends_the_discount()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Forever, planCode: "pro"));

        subscription.EndDiscountIfPlanNotCovered("pro", Jan1.AddDays(3));
        Assert.NotNull(subscription.ActiveDiscount(Jan1.AddDays(3)));

        subscription.EndDiscountIfPlanNotCovered("team", Jan1.AddDays(4));
        Assert.Null(subscription.ActiveDiscount(Jan1.AddDays(4)));
        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddMonths(1)));
    }

    // El cobro del upgrade se mide contra el plan destino: un descuento limitado al plan viejo no lo cubre.
    [Fact]
    public void A_discount_limited_to_a_plan_does_not_cover_a_charge_for_another_plan()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Forever, planCode: "pro"));

        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddDays(1), "team"));
        Assert.Equal(2_000, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddDays(1), "pro"));
    }

    // Un código de un solo cobro que espera la renovación se usa en el próximo cobro, aunque llegue antes (el upgrade).
    [Fact]
    public void A_pending_once_code_covers_the_next_charge_before_its_date()
    {
        var subscription = WithDiscount(Percent(3_000, DiscountDuration.Once));
        var beforeRenewal = Jan1.AddDays(-10);

        Assert.Equal(
            3_000,
            subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", beforeRenewal, nextCharge: true)
        );
        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", beforeRenewal));
    }

    [Fact]
    public void A_once_code_used_on_the_upgrade_is_consumed()
    {
        var subscription = WithDiscount(Percent(3_000, DiscountDuration.Once));

        subscription.ConsumePendingOnceDiscount(Jan1.AddDays(-10));

        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1));
        Assert.NotNull(Assert.Single(subscription.Discounts).EndedAtUtc);
    }

    [Fact]
    public void Base_only_discount_does_not_touch_seats()
    {
        var baseOnly = WithDiscount(Percent(2_000, DiscountDuration.Forever, appliesTo: DiscountAppliesTo.Base));
        var everything = WithDiscount(
            Percent(2_000, DiscountDuration.Forever, appliesTo: DiscountAppliesTo.BaseSeatsAddOns)
        );

        Assert.Equal(0, baseOnly.DiscountFor(DiscountChargeKind.Seats, 1_500, "USD", Jan1.AddMonths(1)));
        Assert.Equal(300, everything.DiscountFor(DiscountChargeKind.Seats, 1_500, "USD", Jan1.AddMonths(1)));
    }

    [Fact]
    public void Admin_discount_requires_a_reason()
    {
        var terms = SubscriptionDiscountTerms.Create(
            DiscountSource.Admin,
            codeDefinitionId: null,
            percentBasisPoints: 2_000,
            amountCents: null,
            currency: null,
            DiscountDuration.Forever,
            months: null,
            DiscountAppliesTo.Base,
            planCode: null,
            reason: "  ",
            Jan1
        );

        Assert.Equal("Subscription.Discount.ReasonRequired", terms.Error.Code);
    }

    [Fact]
    public void An_ended_discount_no_longer_applies()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Forever));

        Assert.True(subscription.EndDiscount("Retention deal finished", Admin, Jan1.AddMonths(2)).IsSuccess);

        Assert.Equal(0, subscription.DiscountFor(DiscountChargeKind.Base, 10_000, "USD", Jan1.AddMonths(3)));
        Assert.Equal(
            "Subscription.Discount.NotActive",
            subscription.EndDiscount("again", Admin, Jan1.AddMonths(3)).Error.Code
        );
    }

    [Fact]
    public void A_finished_window_is_closed_once()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Months, months: 3));
        var discount = Assert.Single(subscription.Discounts);

        Assert.False(subscription.CloseExpiredDiscounts(Jan1.AddMonths(2)));
        Assert.True(subscription.CloseExpiredDiscounts(Jan1.AddMonths(3)));
        Assert.Equal(Jan1.AddMonths(3), discount.EndedAtUtc);
        Assert.Equal("Expired.", discount.EndedReason);
        Assert.False(subscription.CloseExpiredDiscounts(Jan1.AddMonths(4)));
    }

    // El recibo nombra el descuento: el código enmascarado (nunca en claro) o "Discount" si lo dio la plataforma.
    [Fact]
    public void A_code_discount_reads_with_its_masked_code()
    {
        var terms = SubscriptionDiscountTerms
            .Create(
                DiscountSource.Code,
                Guid.NewGuid(),
                2_000,
                null,
                null,
                DiscountDuration.Forever,
                null,
                DiscountAppliesTo.Base,
                null,
                null,
                Jan1,
                codeDisplay: " SPRI...0420 "
            )
            .Value;

        var discount = Assert.Single(WithDiscount(terms).Discounts);

        Assert.Equal("SPRI...0420", discount.CodeDisplay);
        Assert.Equal("Promo code SPRI...0420 · 20% off", discount.ReceiptLabel());
    }

    [Fact]
    public void A_platform_discount_reads_as_discount_with_its_benefit()
    {
        Assert.Equal(
            "Discount · 12.5% off",
            Assert.Single(WithDiscount(Percent(1_250, DiscountDuration.Forever)).Discounts).ReceiptLabel()
        );
        Assert.Equal(
            "Discount · $5.00 off",
            Assert.Single(WithDiscount(Fixed(500, "USD", DiscountDuration.Forever)).Discounts).ReceiptLabel()
        );
    }

    [Fact]
    public void The_charge_carries_the_label_of_the_discount_it_used()
    {
        var subscription = WithDiscount(Percent(2_000, DiscountDuration.Forever));

        var (cents, source, label) = subscription.DiscountWithSourceFor(DiscountChargeKind.Base, 1_000, "USD", Jan1);

        Assert.Equal((200L, DiscountSource.Admin, "Discount · 20% off"), (cents, source, label));
    }

    private static SubscriptionDiscountTerms Percent(
        int basisPoints,
        DiscountDuration duration,
        int? months = null,
        string? planCode = null,
        DiscountAppliesTo appliesTo = DiscountAppliesTo.Base
    ) =>
        SubscriptionDiscountTerms
            .Create(
                DiscountSource.Admin,
                null,
                basisPoints,
                null,
                null,
                duration,
                months,
                appliesTo,
                planCode,
                "Retention deal",
                Jan1
            )
            .Value;

    private static SubscriptionDiscountTerms Fixed(long cents, string currency, DiscountDuration duration) =>
        SubscriptionDiscountTerms
            .Create(
                DiscountSource.Admin,
                null,
                null,
                cents,
                currency,
                duration,
                null,
                DiscountAppliesTo.Base,
                null,
                "Retention deal",
                Jan1
            )
            .Value;

    private static TenantSubscription WithDiscount(SubscriptionDiscountTerms terms)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, Jan1)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        plan.AddVersion(version, Guid.Empty, Jan1);
        plan.PublishVersion(version.Id, Jan1, Guid.Empty, Jan1);
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                Jan1,
                Jan1.AddMonths(1),
                Guid.Empty,
                Jan1
            )
            .Value;

        Assert.True(subscription.GrantDiscount(terms, replace: false, Admin, Jan1).IsSuccess);
        return subscription;
    }
}
