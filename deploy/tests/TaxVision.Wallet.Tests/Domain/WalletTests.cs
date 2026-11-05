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
}
