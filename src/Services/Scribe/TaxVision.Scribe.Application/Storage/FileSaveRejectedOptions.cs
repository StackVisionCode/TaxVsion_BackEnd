namespace TaxVision.Scribe.Application.Storage;

/// <summary>Ventana en la que un rechazo sin referencias se toma por adelantado y se reintenta (SCR-05).</summary>
public sealed class FileSaveRejectedOptions
{
    public const string SectionName = "Scribe:FileSaveRejected";

    // Cierra antes del último reintento de BB-7 (12 min 35 s), para que el huérfano acabe en Warning y no en la DLQ.
    public TimeSpan EarlyRejectionWindow { get; set; } = TimeSpan.FromMinutes(10);
}
