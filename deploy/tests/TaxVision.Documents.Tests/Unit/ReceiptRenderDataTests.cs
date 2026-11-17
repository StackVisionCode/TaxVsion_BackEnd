using TaxVision.Documents.Application.Generations.Receipts;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>
/// P7: el recibo dice el día del pago en la zona de quien paga, sin hora (como los recibos de la industria); sin zona,
/// el día en UTC.
/// </summary>
public sealed class ReceiptRenderDataTests
{
    private static readonly DateTime Paid = new(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_paid_at_is_the_date_only()
    {
        Assert.Equal("Nov 18, 2026", ReceiptRenderData.PaidAt(Paid, "America/New_York"));
        Assert.Equal("Nov 18, 2026", ReceiptRenderData.PaidAt(Paid, null));
    }

    /// <summary>Un pago a la 1:00 UTC del 1-ene es del 31-dic para quien paga en Nueva York.</summary>
    [Fact]
    public void The_paid_at_is_the_day_in_the_payer_zone()
    {
        var lateNight = new DateTime(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Dec 31, 2026", ReceiptRenderData.PaidAt(lateNight, "America/New_York"));
        Assert.Equal("Jan 1, 2027", ReceiptRenderData.PaidAt(lateNight, null));
    }

    /// <summary>La carpeta del año: un cobro del 31-dic a la noche en Nueva York es del año que termina.</summary>
    [Fact]
    public void The_paid_day_is_the_payer_civil_day()
    {
        Assert.Equal(
            new DateOnly(2026, 12, 31),
            ReceiptRenderData.PaidDay(new DateTime(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc), "America/New_York")
        );
    }

    /// <summary>Con descuento: subtotal bruto, una fila por descuento y el total pagado aparte.</summary>
    [Fact]
    public void The_totals_show_the_gross_subtotal_and_each_discount()
    {
        var totals = ReceiptRenderData.Totals(1_200, [new ReceiptDiscount("Promo code SPRI...0420 · 20% off", 300)]);

        Assert.Equal("15.00", totals["subtotal"]);
        Assert.Equal(true, totals["hasDiscounts"]);
        var row = Assert.Single((List<Dictionary<string, object>>)totals["discounts"]);
        Assert.Equal("Promo code SPRI...0420 · 20% off", row["label"]);
        Assert.Equal("3.00", row["amount"]);
    }

    [Fact]
    public void Without_discounts_the_subtotal_is_what_was_paid()
    {
        var totals = ReceiptRenderData.Totals(1_200, [new ReceiptDiscount("Ignored", 0)]);

        Assert.Equal("12.00", totals["subtotal"]);
        Assert.Equal(false, totals["hasDiscounts"]);
        Assert.Empty((List<Dictionary<string, object>>)totals["discounts"]);
    }
}
