namespace TaxVision.Billing.Infrastructure.Scheduling;

/// <summary>Job de reconciliación del enlace de cobro y del stock (BIL-3b).</summary>
public sealed class InvoiceSyncReconciliationOptions
{
    public const string SectionName = "Billing:SyncReconciliation";

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan Grace { get; set; } = TimeSpan.FromMinutes(15);
    public int BatchSize { get; set; } = 100;

    /// <summary>Fecha de corte: lo anterior es reposición histórica y la decide el dueño. Sin valor, el job no corre.</summary>
    public DateTime? NotBeforeUtc { get; set; }
}
