using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// La reserva del código vive 24 h pero el reintento del pago dura más: al reintentar con una reserva vencida
/// se renueva con los mismos códigos, solo si el descuento no cambia (el neto del pago ya está fijado).
/// </summary>
public sealed class OnboardingCodeReservationRenewalTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static TenantOnboarding WithPromo(DateTime? expiresAtUtc, long discountCents = 2_580)
    {
        var onboarding = TenantOnboarding
            .Create(
                "owner@castillotax.com",
                Now.AddDays(-2),
                Guid.NewGuid(),
                "Carlos",
                "Castillo",
                null,
                Now.AddDays(-2)
            )
            .Value;
        onboarding.ApplyOnboardingPricing(
            12_900,
            discountCents,
            12_900 - discountCents,
            "USD",
            [
                new OnboardingCodeReservationInput(
                    Guid.NewGuid(),
                    OnboardingBenefitType.Promo,
                    "WELCOME20",
                    discountCents,
                    "snap-1",
                    expiresAtUtc,
                    Guid.NewGuid()
                ),
            ],
            Now.AddDays(-2)
        );
        return onboarding;
    }

    private static OnboardingCodeReservationInput Renewed(long discountCents = 2_580) =>
        new(
            Guid.NewGuid(),
            OnboardingBenefitType.Promo,
            "WELCOME20",
            discountCents,
            "snap-2",
            Now.AddHours(24),
            Guid.NewGuid()
        );

    [Fact]
    public void A_reservation_past_its_expiry_counts_as_expired()
    {
        Assert.True(WithPromo(Now.AddMinutes(-1)).HasExpiredCodeReservations(Now));
        Assert.False(WithPromo(Now.AddHours(1)).HasExpiredCodeReservations(Now));
    }

    [Fact]
    public void A_reservation_saved_without_expiry_counts_as_expired_after_24_hours()
    {
        // Reservas guardadas antes de este cambio: no tienen vencimiento; vivían 24 h desde que se crearon.
        Assert.True(WithPromo(null).HasExpiredCodeReservations(Now));
    }

    [Fact]
    public void Renewal_replaces_the_expired_reservations_and_keeps_the_net()
    {
        var onboarding = WithPromo(Now.AddMinutes(-1));
        var renewed = Renewed();

        var result = onboarding.RenewCodeReservations([renewed], Now);

        Assert.True(result.IsSuccess);
        var reservation = Assert.Single(onboarding.CodeReservations);
        Assert.Equal(renewed.CodeReservationId, reservation.CodeReservationId);
        Assert.Equal(renewed.PaymentReferenceId, reservation.PaymentReferenceId);
        Assert.Equal(renewed.ExpiresAtUtc, reservation.ExpiresAtUtc);
        Assert.Equal(0, reservation.Order);
        Assert.Equal(10_320, onboarding.NetAmountCents);
        Assert.Equal(1, onboarding.PricingGeneration);
    }

    [Fact]
    public void Renewal_is_rejected_while_the_reservations_are_still_valid()
    {
        var onboarding = WithPromo(Now.AddHours(1));

        var result = onboarding.RenewCodeReservations([Renewed()], Now);

        Assert.Equal("Onboarding.ReservationsStillValid", result.Error.Code);
        Assert.Equal(0, onboarding.PricingGeneration);
    }

    [Fact]
    public void A_different_discount_cannot_replace_the_reservation()
    {
        var onboarding = WithPromo(Now.AddMinutes(-1));
        var original = onboarding.CodeReservations.Single().CodeReservationId;

        var result = onboarding.RenewCodeReservations([Renewed(discountCents: 1_000)], Now);

        Assert.Equal("Onboarding.CodeNoLongerAvailable", result.Error.Code);
        Assert.Equal(original, onboarding.CodeReservations.Single().CodeReservationId);
    }

    [Fact]
    public void A_missing_code_cannot_replace_the_reservation()
    {
        var onboarding = WithPromo(Now.AddMinutes(-1));

        var result = onboarding.RenewCodeReservations([], Now);

        Assert.Equal("Onboarding.CodeNoLongerAvailable", result.Error.Code);
    }

    [Fact]
    public void Renewal_requires_the_onboarding_to_wait_for_payment()
    {
        var onboarding = WithPromo(Now.AddMinutes(-1));
        onboarding.MarkPaymentProcessing(Guid.NewGuid(), "cs_test");

        var result = onboarding.RenewCodeReservations([Renewed()], Now);

        Assert.True(result.IsFailure);
    }
}
