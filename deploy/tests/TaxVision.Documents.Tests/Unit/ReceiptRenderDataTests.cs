using TaxVision.Documents.Application.Generations.Receipts;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>P7: el recibo dice la fecha y la hora en la zona de quien paga; sin zona, UTC rotulado.</summary>
public sealed class ReceiptRenderDataTests
{
    private static readonly DateTime Paid = new(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_paid_at_is_written_in_the_payer_zone()
    {
        Assert.Equal(
            "Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5",
            ReceiptRenderData.PaidAt(Paid, "America/New_York")
        );
    }

    [Fact]
    public void Without_a_zone_the_paid_at_is_labelled_utc()
    {
        Assert.Equal("Wed, Nov 18, 2026, 2:00 PM · UTC", ReceiptRenderData.PaidAt(Paid, null));
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
}
