using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// Gift/Promo/Referral en el onboarding pago-primero — pricing del agregado (ApplyOnboardingPricing +
/// MarkFullyCoveredByCode) y la referencia de pago determinística por reserva apilada
/// (OnboardingPaymentReference). Cubre: descuento parcial, apilado (N reservas con Order), tope al bruto,
/// $0 cubierto 100%, idempotencia y validaciones.
/// </summary>
public sealed class OnboardingPricingTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static TenantOnboarding Valid() =>
        TenantOnboarding.Create("owner@castillotax.com", Now, Guid.NewGuid(), "Carlos", "Castillo", null, Now).Value;

    private static OnboardingCodeReservationInput Reservation(
        OnboardingBenefitType type,
        string code,
        long discountCents
    ) => new(Guid.NewGuid(), type, code, discountCents, "snapshot-" + code);

    // Cada código del alta se lee en el recibo por su tipo y su código enmascarado, nunca en claro.
    [Fact]
    public void The_receipt_lists_each_code_with_its_masked_display()
    {
        var onboarding = Valid();
        onboarding.ApplyOnboardingPricing(
            grossCents: 12900,
            totalDiscountCents: 3480,
            netCents: 9420,
            currency: "USD",
            reservations:
            [
                new(Guid.NewGuid(), OnboardingBenefitType.Promo, "SPRING20", 2580, "s1", CodeDisplay: "SPRI...NG20"),
                new(
                    Guid.NewGuid(),
                    OnboardingBenefitType.Gift,
                    "GIFT-SECRET-7F2A",
                    900,
                    "s2",
                    CodeDisplay: "GIFT...7F2A"
                ),
            ],
            nowUtc: Now
        );

        Assert.Equal(
            [("Promo code SPRI...NG20", 2580L), ("Gift card GIFT...7F2A", 900L)],
            onboarding.ReceiptDiscounts().Select(discount => (discount.Label, discount.AmountCents))
        );
        Assert.DoesNotContain(onboarding.ReceiptDiscounts(), discount => discount.Label.Contains("SECRET"));
    }

    [Fact]
    public void The_registration_email_carries_the_list_price_and_each_code()
    {
        var onboarding = Valid();
        onboarding.ApplyOnboardingPricing(
            grossCents: 12900,
            totalDiscountCents: 3480,
            netCents: 9420,
            currency: "USD",
            reservations:
            [
                new(Guid.NewGuid(), OnboardingBenefitType.Promo, "SPRING20", 2580, "s1", CodeDisplay: "SPRI...NG20"),
                new(
                    Guid.NewGuid(),
                    OnboardingBenefitType.Gift,
                    "GIFT-SECRET-7F2A",
                    900,
                    "s2",
                    CodeDisplay: "GIFT...7F2A"
                ),
            ],
            nowUtc: Now
        );

        var email = OnboardingEmailPricing.Discount(onboarding);

        Assert.Equal("129.00 USD", email.GrossFormatted);
        Assert.Equal("34.80 USD", email.DiscountFormatted);
        Assert.Equal("Promo code SPRI...NG20 + Gift card GIFT...7F2A", email.DiscountSummary);
    }

    [Fact]
    public void A_registration_email_without_codes_carries_no_discount()
    {
        var email = OnboardingEmailPricing.Discount(Valid());

        Assert.Equal((null, null, null), (email.GrossFormatted, email.DiscountFormatted, email.DiscountSummary));
    }

    [Fact]
    public void A_code_of_n_months_keeps_its_recurring_discount_for_the_subscription()
    {
        var onboarding = Valid();
        var recurring = new OnboardingRecurringDiscount(Guid.NewGuid(), 2_000, null, "USD", "Months", 3);

        onboarding.ApplyOnboardingPricing(
            grossCents: 12900,
            totalDiscountCents: 2580,
            netCents: 10320,
            currency: "USD",
            reservations:
            [
                new(Guid.NewGuid(), OnboardingBenefitType.Gift, "GIFT", 0, "snapshot-gift"),
                new(Guid.NewGuid(), OnboardingBenefitType.Promo, "STAY20", 2580, "snapshot-stay", Recurring: recurring),
            ],
            nowUtc: Now
        );

        Assert.Equal(recurring, onboarding.RecurringDiscount());
        Assert.Null(Valid().RecurringDiscount());
    }

    // ---------- ApplyOnboardingPricing ----------

    [Fact]
    public void ApplyOnboardingPricing_partial_sets_breakdown_and_one_reservation()
    {
        var onboarding = Valid();

        var result = onboarding.ApplyOnboardingPricing(
            grossCents: 12900,
            totalDiscountCents: 2580,
            netCents: 10320,
            currency: "USD",
            reservations: [Reservation(OnboardingBenefitType.Referral, "WELCOME20", 2580)],
            nowUtc: Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(12900, onboarding.GrossAmountCents);
        Assert.Equal(2580, onboarding.TotalDiscountCents);
        Assert.Equal(10320, onboarding.NetAmountCents);
        Assert.Equal("USD", onboarding.Currency);
        Assert.False(onboarding.FullyCovered);
        Assert.Single(onboarding.CodeReservations);
        Assert.Equal(0, onboarding.CodeReservations.Single().Order);
    }

    [Fact]
    public void ApplyOnboardingPricing_stacked_assigns_order_by_list_position()
    {
        var onboarding = Valid();

        var result = onboarding.ApplyOnboardingPricing(
            grossCents: 12900,
            totalDiscountCents: 12900,
            netCents: 0,
            currency: "USD",
            reservations:
            [
                Reservation(OnboardingBenefitType.Referral, "WELCOME20", 2580),
                Reservation(OnboardingBenefitType.Gift, "WELCOME100", 10320),
            ],
            nowUtc: Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, onboarding.CodeReservations.Count);
        var ordered = onboarding.CodeReservations.OrderBy(r => r.Order).ToList();
        Assert.Equal(0, ordered[0].Order);
        Assert.Equal("WELCOME20", ordered[0].Code);
        Assert.Equal(1, ordered[1].Order);
        Assert.Equal("WELCOME100", ordered[1].Code);
    }

    [Fact]
    public void ApplyOnboardingPricing_net_zero_marks_fully_covered()
    {
        var onboarding = Valid();

        onboarding.ApplyOnboardingPricing(
            12900,
            12900,
            0,
            "USD",
            [Reservation(OnboardingBenefitType.Gift, "WELCOME100", 12900)],
            Now
        );

        Assert.True(onboarding.FullyCovered);
    }

    [Fact]
    public void ApplyOnboardingPricing_rejects_negative_amounts()
    {
        var onboarding = Valid();

        var result = onboarding.ApplyOnboardingPricing(-1, 0, -1, "USD", [], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidAmount", result.Error.Code);
    }

    [Fact]
    public void ApplyOnboardingPricing_rejects_discount_greater_than_gross()
    {
        var onboarding = Valid();

        var result = onboarding.ApplyOnboardingPricing(
            grossCents: 10000,
            totalDiscountCents: 12000,
            netCents: 0,
            currency: "USD",
            reservations: [Reservation(OnboardingBenefitType.Gift, "TOOBIG", 12000)],
            nowUtc: Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.DiscountExceedsGross", result.Error.Code);
    }

    [Fact]
    public void ApplyOnboardingPricing_rejects_net_not_equal_gross_minus_discount()
    {
        var onboarding = Valid();

        var result = onboarding.ApplyOnboardingPricing(
            grossCents: 12900,
            totalDiscountCents: 2000,
            netCents: 9999, // debería ser 10900
            currency: "USD",
            reservations: [Reservation(OnboardingBenefitType.Promo, "P", 2000)],
            nowUtc: Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidNet", result.Error.Code);
    }

    [Fact]
    public void ApplyOnboardingPricing_rejects_reservation_sum_mismatch()
    {
        var onboarding = Valid();

        var result = onboarding.ApplyOnboardingPricing(
            grossCents: 12900,
            totalDiscountCents: 2580,
            netCents: 10320,
            currency: "USD",
            // La suma de reservas (2000) no coincide con el descuento total (2580).
            reservations: [Reservation(OnboardingBenefitType.Referral, "WELCOME20", 2000)],
            nowUtc: Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.AdjustmentMismatch", result.Error.Code);
    }

    [Fact]
    public void ApplyOnboardingPricing_is_idempotent_on_replay()
    {
        var onboarding = Valid();
        onboarding.ApplyOnboardingPricing(
            12900,
            2580,
            10320,
            "USD",
            [Reservation(OnboardingBenefitType.Referral, "WELCOME20", 2580)],
            Now
        );

        // Segundo intento (replay del checkout): no re-reserva, devuelve éxito sin duplicar.
        var result = onboarding.ApplyOnboardingPricing(
            12900,
            2580,
            10320,
            "USD",
            [Reservation(OnboardingBenefitType.Referral, "WELCOME20", 2580)],
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Single(onboarding.CodeReservations);
    }

    [Fact]
    public void ApplyOnboardingPricing_rejects_wrong_state()
    {
        var onboarding = Valid();
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test_1"); // ya no está en PendingPayment

        var result = onboarding.ApplyOnboardingPricing(12900, 0, 12900, "USD", [], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- MarkFullyCoveredByCode ----------

    [Fact]
    public void MarkFullyCoveredByCode_transitions_to_payment_completed()
    {
        var onboarding = Valid();
        onboarding.ApplyOnboardingPricing(
            12900,
            12900,
            0,
            "USD",
            [Reservation(OnboardingBenefitType.Gift, "WELCOME100", 12900)],
            Now
        );

        var result = onboarding.MarkFullyCoveredByCode(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentCompleted, onboarding.Status);
        Assert.Equal("CoveredByCode", onboarding.PaymentStatus);
        Assert.Null(onboarding.PaymentId);
    }

    [Fact]
    public void MarkFullyCoveredByCode_is_idempotent()
    {
        var onboarding = Valid();
        onboarding.ApplyOnboardingPricing(
            12900,
            12900,
            0,
            "USD",
            [Reservation(OnboardingBenefitType.Gift, "WELCOME100", 12900)],
            Now
        );
        onboarding.MarkFullyCoveredByCode(Now);

        var result = onboarding.MarkFullyCoveredByCode(Now.AddSeconds(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentCompleted, onboarding.Status);
    }

    [Fact]
    public void MarkFullyCoveredByCode_rejects_when_net_is_positive()
    {
        var onboarding = Valid();
        onboarding.ApplyOnboardingPricing(
            12900,
            2580,
            10320,
            "USD",
            [Reservation(OnboardingBenefitType.Referral, "WELCOME20", 2580)],
            Now
        );

        var result = onboarding.MarkFullyCoveredByCode(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.NotFullyCovered", result.Error.Code);
    }

    [Fact]
    public void MarkFullyCoveredByCode_rejects_wrong_state()
    {
        var onboarding = Valid();
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test_1");

        var result = onboarding.MarkFullyCoveredByCode(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.InvalidState", result.Error.Code);
    }

    // ---------- OnboardingPaymentReference ----------

    [Fact]
    public void PaymentReference_is_deterministic_for_same_onboarding_and_order()
    {
        var onboardingId = Guid.NewGuid();

        Assert.Equal(OnboardingPaymentReference.For(onboardingId, 0), OnboardingPaymentReference.For(onboardingId, 0));
    }

    [Fact]
    public void PaymentReference_differs_by_order()
    {
        var onboardingId = Guid.NewGuid();

        Assert.NotEqual(
            OnboardingPaymentReference.For(onboardingId, 0),
            OnboardingPaymentReference.For(onboardingId, 1)
        );
    }

    [Fact]
    public void PaymentReference_differs_by_onboarding()
    {
        Assert.NotEqual(
            OnboardingPaymentReference.For(Guid.NewGuid(), 0),
            OnboardingPaymentReference.For(Guid.NewGuid(), 0)
        );
    }

    [Fact]
    public void PaymentReference_is_never_empty()
    {
        Assert.NotEqual(Guid.Empty, OnboardingPaymentReference.For(Guid.NewGuid(), 0));
    }
}
