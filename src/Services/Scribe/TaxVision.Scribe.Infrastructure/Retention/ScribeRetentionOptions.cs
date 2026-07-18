namespace TaxVision.Scribe.Infrastructure.Retention;

public sealed class ScribeRetentionOptions
{
    public const string SectionName = "Scribe:Retention";

    /// <summary>Habilita o desactiva la purga (default: false — se activa cuando el negocio lo autorice).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Días de retención desde la creación de la versión antes de considerarla candidata.</summary>
    public int RetentionDays { get; set; } = 180;

    /// <summary>Cantidad de templates por iteración para no saturar la BD.</summary>
    public int BatchSize { get; set; } = 50;
}
