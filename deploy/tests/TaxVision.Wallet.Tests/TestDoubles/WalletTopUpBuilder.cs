using TaxVision.Wallet.Domain.Wallet;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>Órdenes de recarga para pruebas; por reflexión arma las que Create ya no permite (legado).</summary>
public static class WalletTopUpBuilder
{
    public static WalletTopUp Pending(Guid tenantId, long amountCents = 10_000) =>
        WalletTopUp.Create(tenantId, amountCents, "USD", Guid.NewGuid()).Value;

    public static WalletTopUp PendingLegacy(Guid tenantId, long amountCents, string currency)
    {
        var topUp = Pending(tenantId);
        typeof(WalletTopUp).GetProperty(nameof(WalletTopUp.AmountCents))!.SetValue(topUp, amountCents);
        typeof(WalletTopUp).GetProperty(nameof(WalletTopUp.Currency))!.SetValue(topUp, currency);
        return topUp;
    }
}
