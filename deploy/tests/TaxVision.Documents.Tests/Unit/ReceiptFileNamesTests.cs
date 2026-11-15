using TaxVision.Documents.Application.Generations;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

public sealed class ReceiptFileNamesTests
{
    [Theory]
    [InlineData("TaxOffix", "TaxOffix_Receipt_2026-10-13.pdf")]
    [InlineData("Tax Offix & Co.", "TaxOffixCo_Receipt_2026-10-13.pdf")]
    [InlineData("", "Platform_Receipt_2026-10-13.pdf")]
    public void The_file_name_carries_the_platform_brand_without_unsafe_characters(string brand, string expected)
    {
        Assert.Equal(expected, ReceiptFileNames.For(brand, new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc)));
    }

    /// <summary>P7: el nombre lleva el día del cobro para la oficina; 20:00 del 31-dic en Nueva York es el 31.</summary>
    [Fact]
    public void The_file_name_uses_the_payer_day()
    {
        Assert.Equal(
            "TaxOffix_Receipt_2026-12-31.pdf",
            ReceiptFileNames.For("TaxOffix", new DateTime(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc), "America/New_York")
        );
    }
}
