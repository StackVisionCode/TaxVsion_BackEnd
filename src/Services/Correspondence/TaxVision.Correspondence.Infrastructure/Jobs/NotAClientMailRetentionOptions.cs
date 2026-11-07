namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>COR-18 (D-COR-5): retención del correo de quien no es cliente.</summary>
public sealed class NotAClientMailRetentionOptions
{
    public const string SectionName = "Correspondence:NotAClientRetention";

    /// <summary>Apagado en código; encendido en appsettings.json desde 2026-11-07 (D-COR-5).</summary>
    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 90;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    public int BatchSize { get; set; } = 200;

    /// <summary>Tope por pasada: un atraso grande sigue en la siguiente.</summary>
    public int MaxBatchesPerRun { get; set; } = 25;
}
