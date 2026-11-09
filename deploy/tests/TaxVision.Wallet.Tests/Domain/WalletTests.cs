using TaxVision.Wallet.Domain.ValueObjects;
using TaxVision.Wallet.Domain.Wallet;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Domain;

public sealed class WalletTests
{
    [Fact]
    public void Create_NewWallet_HasZeroBalances()
    {
        var result = WalletAggregate.Create(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.PostedMicros);
        Assert.Equal(0, result.Value.HeldMicros);
        Assert.Equal("USD", result.Value.Currency);
        Assert.Equal(WalletStatus.Active, result.Value.Status);
    }

    [Fact]
    public void Movements_InAnotherCurrency_AreRejectedWithoutTouchingTheBalance()
    {
        var wallet = WalletAggregate.Create(Guid.NewGuid()).Value;
        wallet.Credit(Usd(1_000), "topup:seed");
        wallet.Reserve(Usd(500), "reserve:seed");
        var mxn = WalletMoney.Create(100, "MXN").Value;

        Assert.Equal(WalletErrors.CurrencyMismatch, wallet.Credit(mxn, "a").Error);
        Assert.Equal(WalletErrors.CurrencyMismatch, wallet.Reserve(mxn, "b").Error);
        Assert.Equal(WalletErrors.CurrencyMismatch, wallet.Consume(mxn, "c").Error);
        Assert.Equal(WalletErrors.CurrencyMismatch, wallet.Release(mxn, "d").Error);
        Assert.Equal(WalletErrors.CurrencyMismatch, wallet.Refund(mxn, "e").Error);
        Assert.Equal(WalletErrors.CurrencyMismatch, wallet.Adjust(mxn, "f").Error);
        Assert.Equal(WalletErrors.CurrencyMismatch, wallet.EnsureHoldCovers(mxn).Error);
        Assert.Equal(1_000, wallet.PostedMicros);
        Assert.Equal(500, wallet.HeldMicros);
    }

    [Fact]
    public void EnsureHoldCovers_MoreThanHeld_ReturnsInsufficientHold()
    {
        var wallet = WalletAggregate.Create(Guid.NewGuid()).Value;
        wallet.Credit(Usd(1_000), "topup:seed");
        wallet.Reserve(Usd(500), "reserve:seed");

        Assert.True(wallet.EnsureHoldCovers(Usd(500)).IsSuccess);
        Assert.Equal(WalletErrors.InsufficientHold, wallet.EnsureHoldCovers(Usd(501)).Error);
    }
}
