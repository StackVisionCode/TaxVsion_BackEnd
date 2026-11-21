namespace TaxVision.CloudStorage.Application.Configuration;

/// <summary>Reclamo periódico de orígenes: pregunta a los servicios dueños por los archivos sin origen declarado.</summary>
public sealed class FileSourceClaimOptions
{
    public const string SectionName = "CloudStorage:FileSourceClaims";

    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 24;

    /// <summary>Ids por mensaje; nunca pasa de <c>FileSourcesClaimRequestedIntegrationEvent.MaxFileIds</c>.</summary>
    public int BatchSize { get; set; } = 500;
}
