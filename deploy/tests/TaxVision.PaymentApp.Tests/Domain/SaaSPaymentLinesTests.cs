using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

/// <summary>
/// Un cobro con varias cosas (pasar a anual: plan, add-ons y asientos) guarda una línea por cada una para que el
/// recibo las detalle. Las líneas tienen que sumar exactamente lo cobrado.
/// </summary>
public sealed class SaaSPaymentLinesTests
{
    [Fact]
    public void The_payment_keeps_one_line_per_charged_item_and_shares_them_in_its_snapshot()
    {
        var payment = PlanChangePayment(143_900);

        var result = payment.AttachLines([
            SaaSPaymentLine.Create("Enterprise — yearly", 1, 99_900).Value,
            SaaSPaymentLine.Create("Email — yearly", 1, 29_000).Value,
            SaaSPaymentLine.Create("Extra seats (Standard) — yearly", 1, 15_000).Value,
        ]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var lines = payment.Snapshot().Lines!;
        Assert.Equal(3, lines.Count);
        Assert.Equal(
            ("Email — yearly", 1, 29_000L),
            (lines[1].Description, lines[1].Quantity, lines[1].UnitAmountCents)
        );
    }

    [Fact]
    public void Lines_that_do_not_add_up_to_the_amount_are_rejected()
    {
        var payment = PlanChangePayment(143_900);

        var result = payment.AttachLines([SaaSPaymentLine.Create("Enterprise — yearly", 1, 99_900).Value]);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.LinesMismatch", result.Error.Code);
        Assert.Empty(payment.Snapshot().Lines ?? []);
    }

    /// <summary>Con descuento, las líneas van a precio de lista: suman el bruto y el recibo resta el descuento.</summary>
    [Fact]
    public void With_a_discount_the_lines_can_add_up_to_the_gross()
    {
        var payment = DiscountedPayment(netCents: 1_200, discountCents: 300);

        var result = payment.AttachLines([SaaSPaymentLine.Create("Extra seats (Standard)", 1, 1_500).Value]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    [Fact]
    public void With_a_discount_lines_that_match_neither_net_nor_gross_are_rejected()
    {
        var payment = DiscountedPayment(netCents: 1_200, discountCents: 300);

        var result = payment.AttachLines([SaaSPaymentLine.Create("Extra seats (Standard)", 1, 1_400).Value]);

        Assert.Equal("SaaSPayment.LinesMismatch", result.Error.Code);
    }

    [Fact]
    public void The_discount_keeps_its_label_for_the_receipt()
    {
        var payment = DiscountedPayment(1_200, 300, "  Promo code SPRI…0420 · 20% off  ");

        Assert.Equal("Promo code SPRI…0420 · 20% off", payment.DiscountLabel);
        Assert.Equal("Promo code SPRI…0420 · 20% off", payment.Snapshot().DiscountLabel);
    }

    [Fact]
    public void A_label_without_a_discount_is_rejected()
    {
        var created = SaaSPayment.Create(
            Guid.NewGuid(),
            IdempotencyKey.Create($"label-{Guid.NewGuid():N}").Value,
            Money.Create(1_200, "USD").Value,
            SaaSPaymentType.SeatRenewal,
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("TAXVISION SEATS").Value,
            Guid.Empty,
            DateTime.UtcNow,
            discountLabel: "Discount · 20% off"
        );

        Assert.Equal("SaaSPayment.InvalidDiscount", created.Error.Code);
    }

    [Fact]
    public void A_line_needs_a_description_and_at_least_one_unit()
    {
        Assert.True(SaaSPaymentLine.Create(" ", 1, 100).IsFailure);
        Assert.True(SaaSPaymentLine.Create("Seats", 0, 100).IsFailure);
        Assert.True(SaaSPaymentLine.Create("Seats", 1, -1).IsFailure);
    }

    // Un cobro de varias cosas dice a qué asiento o add-on pertenece cada línea.
    [Fact]
    public void A_line_remembers_what_it_pays_for()
    {
        var payment = PlanChangePayment(2_000);
        var seatId = Guid.NewGuid();

        var attached = Result
            .Success(payment)
            .WithLines([
                new SaaSChargeLine("Pro — monthly", 1, 1_500, "Plan", null),
                new SaaSChargeLine("Standard seat", 1, 500, "Seat", seatId),
            ]);

        Assert.True(attached.IsSuccess);
        var seatLine = payment.Snapshot().Lines![1];
        Assert.Equal(("Seat", seatId), (seatLine.TargetKind, seatLine.TargetId));
        Assert.Equal(("Plan", (Guid?)null), (payment.Lines.First().TargetKind, payment.Lines.First().TargetId));
    }

    [Fact]
    public void A_line_target_kind_has_a_size_limit()
    {
        var line = SaaSPaymentLine.Create("Seat", 1, 500, new string('x', 41), Guid.NewGuid());

        Assert.Equal("SaaSPaymentLine.InvalidTarget", line.Error.Code);
    }

    private static SaaSPayment DiscountedPayment(long netCents, long discountCents, string? label = null) =>
        SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"seat-{Guid.NewGuid():N}").Value,
                Money.Create(netCents, "USD").Value,
                SaaSPaymentType.SeatRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SEATS").Value,
                Guid.Empty,
                DateTime.UtcNow,
                discountAmountCents: discountCents,
                discountSource: "Code",
                discountLabel: label
            )
            .Value;

    private static SaaSPayment PlanChangePayment(long amountCents) =>
        SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"plan-change-{Guid.NewGuid():N}").Value,
                Money.Create(amountCents, "USD").Value,
                SaaSPaymentType.PlanChangeCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION PLAN").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
