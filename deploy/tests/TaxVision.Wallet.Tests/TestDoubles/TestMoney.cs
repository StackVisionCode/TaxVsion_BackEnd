using TaxVision.Wallet.Domain.ValueObjects;

namespace TaxVision.Wallet.Tests.TestDoubles;

public static class TestMoney
{
    public static WalletMoney Usd(long micros) => WalletMoney.Create(micros, "USD").Value;
}
