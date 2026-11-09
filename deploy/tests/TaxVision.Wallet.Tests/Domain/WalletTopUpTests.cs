using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Tests.Domain;

public sealed class WalletTopUpTests
{
    [Fact]
    public void Create_NonWalletCurrency_ReturnsCurrencyMismatch()
    {
        var result = WalletTopUp.Create(Guid.NewGuid(), 10_000, "MXN", Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(WalletErrors.CurrencyMismatch, result.Error);
    }

    [Fact]
    public void Create_LowercaseUsd_IsNormalized()
    {
        var result = WalletTopUp.Create(Guid.NewGuid(), 10_000, " usd ", Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.Currency);
    }
}
