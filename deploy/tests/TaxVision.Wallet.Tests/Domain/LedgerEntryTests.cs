using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Domain;

public sealed class LedgerEntryTests
{
    [Fact]
    public void Movements_KeepTheReferenceTypeAsItsOwnColumn()
    {
        var wallet = WalletAggregate.Create(Guid.NewGuid()).Value;
        var runId = Guid.NewGuid();

        var topUp = wallet.Credit(Usd(1_000_000), "topup:x", Guid.NewGuid(), "topup").Value;
        var reserve = wallet.Reserve(Usd(500_000), "wallet-reserve:campaign-run:x", runId, "campaign-run").Value;
        var legacy = wallet.Release(Usd(500_000), "wallet-release:campaign-run:x", runId).Value;

        Assert.Equal("topup", topUp.ReferenceType);
        Assert.Equal("campaign-run", reserve.ReferenceType);
        Assert.Null(legacy.ReferenceType);
    }
}
