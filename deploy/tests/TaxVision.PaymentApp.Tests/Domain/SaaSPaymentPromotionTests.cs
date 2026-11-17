using BuildingBlocks.Messaging.PaymentIntegrationEvents;
using TaxVision.PaymentApp.Application.SaaSPayments.DomainEventHandlers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Tests.Domain;

/// <summary>
/// El pago del alta guarda los códigos con los que se cobró (uno por reserva de Growth) para que un reembolso o un
/// contracargo pueda compensar cada canje. La suma de sus descuentos tiene que cuadrar con bruto menos neto.
/// </summary>
public sealed class SaaSPaymentPromotionTests
{
    private const long Gross = 12_900;
    private const long Net = 9_900;

    [Fact]
    public void Onboarding_payment_keeps_its_promotions()
    {
        var payment = OnboardingPayment();
        var promo = Guid.NewGuid();
        var gift = Guid.NewGuid();

        var result = payment.AttachPromotions([Promotion(promo, 2_000, 1), Promotion(gift, 1_000, 2)], Gross);

        Assert.True(result.IsSuccess);
        Assert.Equal([promo, gift], payment.Promotions.OrderBy(p => p.Order).Select(p => p.CodeReservationId));
    }

    [Fact]
    public void Net_must_equal_gross_minus_promotions()
    {
        var payment = OnboardingPayment();

        var result = payment.AttachPromotions([Promotion(Guid.NewGuid(), 500, 1)], Gross);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.PromotionsMismatch", result.Error.Code);
        Assert.Empty(payment.Promotions);
    }

    [Fact]
    public void The_same_reservation_cannot_appear_twice()
    {
        var payment = OnboardingPayment();
        var reservation = Guid.NewGuid();

        var result = payment.AttachPromotions(
            [Promotion(reservation, 1_500, 1), Promotion(reservation, 1_500, 2)],
            Gross
        );

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void A_retry_renews_the_promotions_when_the_discount_is_the_same()
    {
        var payment = OnboardingPayment();
        Assert.True(payment.AttachPromotions([Promotion(Guid.NewGuid(), 3_000, 1)], Gross).IsSuccess);
        var renewed = Guid.NewGuid();

        Assert.True(payment.RenewPromotions([Promotion(renewed, 3_000, 1)]).IsSuccess);
        Assert.Equal(renewed, Assert.Single(payment.Promotions).CodeReservationId);

        // Un descuento distinto no se cobra con el mismo pago.
        var changed = payment.RenewPromotions([Promotion(Guid.NewGuid(), 2_000, 1)]);
        Assert.True(changed.IsFailure);
        Assert.Equal(renewed, Assert.Single(payment.Promotions).CodeReservationId);
    }

    [Fact]
    public void Refund_and_chargeback_events_carry_the_promotions()
    {
        var promo = Guid.NewGuid();
        var snapshot = SucceededOnboarding([new SaaSPaymentPromotionSnapshot(promo, 3_000, 1)]);

        var refunded = Assert.IsType<PaymentRefundedIntegrationEvent>(
            Assert.Single(
                SaaSPaymentDomainEventHandler.Handle(
                    new SaaSPaymentRefunded(snapshot, Net, Net, "requested_by_customer", DateTime.UtcNow),
                    new Envelope()
                )
            )
        );
        var chargedBack = Assert.IsType<PaymentChargebackChangedIntegrationEvent>(
            Assert.Single(
                SaaSPaymentDomainEventHandler.Handle(
                    new SaaSPaymentChargedBack(snapshot, "fraudulent", DateTime.UtcNow),
                    new Envelope()
                )
            )
        );

        var carried = Assert.Single(refunded.Promotions!);
        Assert.Equal(promo, carried.CodeReservationId);
        Assert.Equal(3_000, carried.DiscountAmountCents);
        Assert.Equal(3_000, refunded.DiscountAmountCents);
        Assert.Equal(promo, Assert.Single(chargedBack.Promotions!).CodeReservationId);
    }

    private static SaaSPayment OnboardingPayment() =>
        SaaSPayment
            .CreateForOnboarding(
                Guid.NewGuid(),
                IdempotencyKey.Create($"onboarding-{Guid.NewGuid():N}").Value,
                Money.Create(Net, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                DateTime.UtcNow
            )
            .Value;

    private static SaaSPaymentPromotion Promotion(Guid reservationId, long discountCents, int order) =>
        SaaSPaymentPromotion.Create(reservationId, discountCents, "snap", order).Value;

    private static SaaSPaymentSnapshot SucceededOnboarding(IReadOnlyList<SaaSPaymentPromotionSnapshot> promotions) =>
        new(
            Guid.NewGuid(),
            Guid.Empty,
            SaaSPaymentType.OnboardingInitial,
            PaymentStatus.Refunded,
            Guid.NewGuid(),
            "onboarding-promotions",
            Net,
            "USD",
            PaymentProviderCode.Stripe,
            "pi_123",
            DateTime.UtcNow,
            null,
            null,
            null,
            1,
            true,
            Guid.Empty,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Promotions: promotions
        );
}
