namespace TaxVision.Wallet.Application.Wallet.Abstractions;

/// <summary>
/// Saldo de un tenant que no coincide con su ledger. Con más de un monedero no es conciliable: el ledger no
/// tiene moneda, así que los montos son sumas sin sentido y solo sirven de referencia.
/// </summary>
public sealed record WalletBalanceMismatch(
    Guid TenantId,
    int WalletCount,
    long PostedMicros,
    long HeldMicros,
    long LedgerPostedMicros,
    long LedgerHeldMicros
)
{
    public bool IsReconcilable => WalletCount == 1;
}
