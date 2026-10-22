using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

public sealed class CardBrandNamesTests
{
    [Theory]
    [InlineData("visa", "Visa")]
    [InlineData("amex", "American Express")]
    [InlineData("mastercard", "Mastercard")]
    [InlineData("someNewBrand", "Somenewbrand")]
    [InlineData(null, "card")]
    [InlineData(" ", "card")]
    public void Shows_the_brand_the_way_customers_know_it(string? brand, string expected)
    {
        Assert.Equal(expected, CardBrandNames.Display(brand));
    }
}
