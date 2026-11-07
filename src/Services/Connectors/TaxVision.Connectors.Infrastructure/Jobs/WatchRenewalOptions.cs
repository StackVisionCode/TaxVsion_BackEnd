namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>Se renueva entre suscripciones; si la instancia muere, otra toma el turno al vencer.</summary>
public sealed class WatchRenewalOptions
{
    public const string SectionName = "Connectors:WatchRenewal";

    public int LeaderLockTtlMinutes { get; set; } = 5;
}
