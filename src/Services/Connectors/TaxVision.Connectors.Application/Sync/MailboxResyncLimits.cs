namespace TaxVision.Connectors.Application.Sync;

/// <summary>Ventana máxima de una re-sincronización (Connectors:Resync:MaxWindowDays, 30 por defecto).</summary>
public sealed record MailboxResyncLimits(int MaxWindowDays)
{
    public const string MaxWindowDaysKey = "Connectors:Resync:MaxWindowDays";

    public static MailboxResyncLimits Default { get; } = new(30);
}
