namespace TaxVision.Wallet.Domain.Wallet;

/// <summary>Estado del monedero. <see cref="Frozen"/> bloquea nuevas reservas, pero un pago ya
/// confirmado se puede acreditar igual (00_Plan §6, wallet congelada).</summary>
public enum WalletStatus
{
    Active = 1,
    Frozen = 2,
}
