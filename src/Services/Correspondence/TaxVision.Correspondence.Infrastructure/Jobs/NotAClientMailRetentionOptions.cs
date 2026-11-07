namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>COR-18 (D-COR-5): retención del correo de quien no es cliente.</summary>
public sealed class NotAClientMailRetentionOptions
{
    public const string SectionName = "Correspondence:NotAClientRetention";

    /// <summary>Apagado por defecto: borra correo de verdad, así que lo enciende el dueño.</summary>
    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 90;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    public int BatchSize { get; set; } = 200;

    /// <summary>Tope por pasada: un atraso grande sigue en la siguiente.</summary>
    public int MaxBatchesPerRun { get; set; } = 25;
}
