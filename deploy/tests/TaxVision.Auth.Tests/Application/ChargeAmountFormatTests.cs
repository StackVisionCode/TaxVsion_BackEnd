using TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

namespace TaxVision.Auth.Tests.Application;

public sealed class ChargeAmountFormatTests
{
    [Theory]
    [InlineData(4900, "USD", "$49.00")]
    [InlineData(4900, "usd", "$49.00")]
    [InlineData(123456, "USD", "$1,234.56")]
    [InlineData(1050, "EUR", "10.50 EUR")]
    [InlineData(1050, "", "$10.50")]
    public void Formats_the_amount_with_its_currency(long cents, string currency, string expected)
    {
        Assert.Equal(expected, ChargeAmountFormat.Format(cents, currency));
    }
}
