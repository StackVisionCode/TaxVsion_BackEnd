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

    [Fact]
    public void A_line_needs_a_description_and_at_least_one_unit()
    {
        Assert.True(SaaSPaymentLine.Create(" ", 1, 100).IsFailure);
        Assert.True(SaaSPaymentLine.Create("Seats", 0, 100).IsFailure);
        Assert.True(SaaSPaymentLine.Create("Seats", 1, -1).IsFailure);
    }

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
