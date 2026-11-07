namespace TaxVision.Wallet.Infrastructure.Scheduling;

/// <summary>Conciliación saldo vs ledger (<c>Wallet:LedgerReconciliation</c>). Solo lee, por eso nace encendida.</summary>
public sealed class WalletLedgerReconciliationOptions
{
    public const string SectionName = "Wallet:LedgerReconciliation";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Más corto que el intervalo: si una réplica muere con el candado, no se pierde la pasada siguiente.</summary>
    public TimeSpan LockTtl { get; set; } = TimeSpan.FromMinutes(10);

    public bool IsValid() => Interval > TimeSpan.Zero && LockTtl > TimeSpan.Zero;
}
