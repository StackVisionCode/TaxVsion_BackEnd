using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

/// <summary>
/// El desglose es lo que permite que el recibo diga "3 × $10.53". Vale más no ponerlo que ponerlo mal: un
/// recibo cuyas cuentas no salen es peor que uno escueto, y perder la compra por un desglose torcido sería
/// peor todavía.
/// </summary>
public sealed class ChargeBreakdownTests
{
    [Fact]
    public void A_breakdown_that_does_not_add_up_to_the_amount_is_rejected()
    {
        var breakdown = ChargeBreakdown.Create(quantity: 3, unitAmountCents: 500).Value;

        var payment = Create(amountCents: 1600, breakdown);

        Assert.True(payment.IsFailure);
        Assert.Equal("SaaSPayment.BreakdownMismatch", payment.Error.Code);
    }

    [Fact]
    public void A_breakdown_that_adds_up_is_kept()
    {
        var breakdown = ChargeBreakdown.Create(quantity: 3, unitAmountCents: 500).Value;

        var payment = Create(amountCents: 1500, breakdown);

        Assert.True(payment.IsSuccess);
        Assert.Equal(3, payment.Value.Breakdown!.Quantity);
        Assert.Equal(500, payment.Value.Breakdown.UnitAmountCents);
    }

    [Fact]
    public void A_charge_without_units_to_count_simply_has_no_breakdown()
    {
        var payment = Create(amountCents: 1500, breakdown: null);

        Assert.True(payment.IsSuccess);
        Assert.Null(payment.Value.Breakdown);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(-1, 500)]
    [InlineData(3, 0)]
    public void Units_and_price_have_to_be_real(int quantity, long unitAmountCents)
    {
        Assert.True(ChargeBreakdown.Create(quantity, unitAmountCents).IsFailure);
    }

    // El contrato interno es opcional y tolerante: unos números torcidos no tumban el cobro, solo se ignoran.
    [Theory]
    [InlineData(null, null, 1500)]
    [InlineData(3, null, 1500)]
    [InlineData(3, 500L, 1600)]
    [InlineData(0, 500L, 1500)]
    public void A_request_whose_numbers_do_not_work_out_is_dropped(int? quantity, long? unit, long amountCents)
    {
        Assert.Null(ChargeBreakdowns.FromRequest(quantity, unit, amountCents));
    }

    [Fact]
    public void A_request_whose_numbers_work_out_becomes_the_breakdown()
    {
        var breakdown = ChargeBreakdowns.FromRequest(quantity: 3, unitAmountCents: 500, amountCents: 1500);

        Assert.NotNull(breakdown);
        Assert.Equal(1500, breakdown!.TotalCents);
    }

    private static BuildingBlocks.Results.Result<SaaSPayment> Create(long amountCents, ChargeBreakdown? breakdown) =>
        SaaSPayment.Create(
            Guid.NewGuid(),
            IdempotencyKey.Create("key-1").Value,
            Money.Create(amountCents, "USD").Value,
            SaaSPaymentType.SeatsPurchaseCharge,
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("TAXVISION SAAS").Value,
            Guid.Empty,
            DateTime.UtcNow,
            breakdown: breakdown
        );
}
