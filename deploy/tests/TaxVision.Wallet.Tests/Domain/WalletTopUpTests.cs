using TaxVision.Wallet.Domain.Settings;
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

    [Fact]
    public void Create_ManualCheckout_KeepsProviderAndMethod()
    {
        var topUp = WalletTopUp.Create(Guid.NewGuid(), 10_000, "USD", Guid.NewGuid(), "PayPal", "PayPal").Value;

        Assert.False(topUp.Automatic);
        Assert.Equal("PayPal", topUp.Provider);
        Assert.Equal("PayPal", topUp.Method);
        Assert.Equal("PayPal", topUp.PaymentLabel);
    }

    [Fact]
    public void CreateAutomatic_KeepsTheAuthorizedMethod()
    {
        var method = AutoRechargeMethod.Create("Stripe", "pm_123", "Visa 4242").Value;

        var topUp = WalletTopUp.CreateAutomatic(Guid.NewGuid(), 2_500, "USD", Guid.NewGuid(), method).Value;

        Assert.True(topUp.Automatic);
        Assert.Equal(WalletTopUpStatus.Pending, topUp.Status);
        Assert.Equal("Stripe", topUp.Provider);
        Assert.Equal("pm_123", topUp.PaymentMethodReference);
        Assert.Equal("Visa 4242", topUp.PaymentLabel);
    }
}
