using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RecordSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>El correo del cobro automático dice qué se cobró y, si hubo descuento, cuánto y con qué código.</summary>
public sealed class RecurringChargeEmailTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_discounted_charge_carries_the_discount_and_what_was_charged()
    {
        var email = RecordSaaSPaymentChargeHandler.RecurringChargeSucceededEmail(
            Payment(discountAmountCents: 300, discountLabel: "Promo code SPRI...0420 · 20% off"),
            "corr",
            Now
        );

        Assert.Equal("12.00", email.Amount);
        Assert.Equal("3.00", email.DiscountAmount);
        Assert.Equal("Promo code SPRI...0420 · 20% off", email.DiscountLabel);
        Assert.Equal("Subscription renewal", email.ItemDescription);
        Assert.Equal(Now, email.DateChargedAtUtc);
    }

    [Fact]
    public void A_charge_without_discount_carries_no_discount()
    {
        var email = RecordSaaSPaymentChargeHandler.RecurringChargeSucceededEmail(Payment(), "corr", Now);

        Assert.Null(email.DiscountAmount);
        Assert.Null(email.DiscountLabel);
    }

    private static SaaSPayment Payment(long? discountAmountCents = null, string? discountLabel = null) =>
        SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"key-{Guid.NewGuid():N}").Value,
                Money.Create(1_200, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                Now,
                discountAmountCents: discountAmountCents,
                discountSource: discountAmountCents is null ? null : "Code",
                discountLabel: discountLabel
            )
            .WithConceptFor(SaaSPaymentType.SubscriptionRenewal)
            .Value;
}

/// <summary>El historial de pagos de la cuenta muestra el descuento de cada cobro con su código enmascarado.</summary>
public sealed class MySaaSPaymentResponseTests
{
    [Fact]
    public void A_discounted_payment_lists_its_discount_and_label()
    {
        var payment = SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"key-{Guid.NewGuid():N}").Value,
                Money.Create(1_200, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow,
                discountAmountCents: 300,
                discountSource: "Code",
                discountLabel: "Promo code SPRI...0420 · 20% off"
            )
            .Value;

        var response = TaxVision.PaymentApp.Application.SaaSPayments.Queries.SearchMySaaSPaymentsHandler.Map(
            payment,
            new Dictionary<SaaSPaymentType, DateTime>(),
            new Dictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime>()
        );

        Assert.Equal(1_200, response.AmountCents);
        Assert.Equal(300, response.DiscountAmountCents);
        Assert.Equal("Promo code SPRI...0420 · 20% off", response.DiscountLabel);
    }

    // La cuenta solo ofrece "Pay now" de un asiento o add-on que siga vencido: necesita saber a cuál cobró.
    [Fact]
    public void A_payment_says_which_seat_or_add_on_it_charged()
    {
        var seatId = Guid.NewGuid();
        var payment = SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"seat-renewal-{Guid.NewGuid():N}").Value,
                Money.Create(1_500, "USD").Value,
                SaaSPaymentType.SeatRenewal,
                seatId,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var response = TaxVision.PaymentApp.Application.SaaSPayments.Queries.SearchMySaaSPaymentsHandler.Map(
            payment,
            new Dictionary<SaaSPaymentType, DateTime>(),
            new Dictionary<(SaaSPaymentType Type, Guid TargetAggregateId), DateTime>()
        );

        Assert.Equal(seatId, response.TargetId);
    }
}
