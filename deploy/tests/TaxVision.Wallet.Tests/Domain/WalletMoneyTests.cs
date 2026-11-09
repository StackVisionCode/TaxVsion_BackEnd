using TaxVision.Wallet.Domain.ValueObjects;
using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Tests.Domain;

public sealed class WalletMoneyTests
{
    [Fact]
    public void Add_SameCurrency_SumsMicros()
    {
        var result = WalletMoney.Create(1_500, "USD").Value.Add(WalletMoney.Create(250, "usd").Value);

        Assert.Equal(1_750, result.Value.Micros);
        Assert.Equal("USD", result.Value.Currency);
    }

    [Fact]
    public void Add_DifferentCurrencies_Fails()
    {
        var result = WalletMoney.Create(1, "USD").Value.Add(WalletMoney.Create(1, "MXN").Value);

        Assert.True(result.IsFailure);
        Assert.Equal(WalletErrors.CurrencyMismatch, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("DOLLAR")]
    public void Create_CurrencyIsNotIso_Fails(string currency)
    {
        var result = WalletMoney.Create(1, currency);

        Assert.True(result.IsFailure);
        Assert.Equal(WalletErrors.CurrencyRequired, result.Error);
    }

    [Fact]
    public void Create_NegativeMicros_KeepsTheSign()
    {
        Assert.Equal(-5, WalletMoney.Create(-5, "USD").Value.Micros);
    }
}
