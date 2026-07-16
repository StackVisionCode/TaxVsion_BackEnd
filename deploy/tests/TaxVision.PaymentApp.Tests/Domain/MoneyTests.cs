using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void Create_with_a_negative_amount_fails()
    {
        var result = Money.Create(-1, "USD");

        Assert.True(result.IsFailure);
        Assert.Equal("Money.NegativeAmount", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Create_with_an_invalid_currency_fails(string currency)
    {
        var result = Money.Create(100, currency);

        Assert.True(result.IsFailure);
        Assert.Equal("Money.InvalidCurrency", result.Error.Code);
    }

    [Fact]
    public void Create_normalizes_currency_to_upper_invariant()
    {
        var result = Money.Create(100, "usd");

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.Currency);
    }

    [Fact]
    public void Add_sums_amounts_of_the_same_currency()
    {
        var a = Money.Create(100, "USD").Value;
        var b = Money.Create(250, "USD").Value;

        var result = a.Add(b);

        Assert.True(result.IsSuccess);
        Assert.Equal(350, result.Value.AmountCents);
    }

    [Fact]
    public void Add_across_different_currencies_fails()
    {
        var a = Money.Create(100, "USD").Value;
        var b = Money.Create(100, "EUR").Value;

        var result = a.Add(b);

        Assert.True(result.IsFailure);
        Assert.Equal("Money.CurrencyMismatch", result.Error.Code);
    }

    [Fact]
    public void Subtract_below_zero_fails()
    {
        var a = Money.Create(100, "USD").Value;
        var b = Money.Create(200, "USD").Value;

        var result = a.Subtract(b);

        Assert.True(result.IsFailure);
        Assert.Equal("Money.NegativeResult", result.Error.Code);
    }
}
