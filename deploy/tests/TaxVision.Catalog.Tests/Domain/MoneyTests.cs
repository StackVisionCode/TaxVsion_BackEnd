using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.ValueObjects;

namespace TaxVision.Catalog.Tests.Domain;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(100.50, "usd", "USD")]
    [InlineData(0, "DOP", "DOP")]
    [InlineData(999999.99, "eur", "EUR")]
    public void Create_accepts_valid_amount_and_uppercases_currency(decimal amount, string currency, string expected)
    {
        var result = Money.Create(amount, currency);

        Assert.True(result.IsSuccess);
        Assert.Equal(amount, result.Value.Amount);
        Assert.Equal(expected, result.Value.Currency);
    }

    [Fact]
    public void Create_rejects_negative_amount()
    {
        var result = Money.Create(-1, "USD");
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.InvalidAmount.Code, result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U1D")]
    public void Create_rejects_invalid_currency(string? currency)
    {
        var result = Money.Create(10, currency);
        Assert.True(result.IsFailure);
        Assert.Equal(CatalogErrors.InvalidCurrency.Code, result.Error.Code);
    }
}
