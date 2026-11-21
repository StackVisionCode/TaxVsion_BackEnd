namespace TaxVision.Mobile.Infrastructure.Retention;

/// <summary>
/// Operación de la purga del recorrido GPS (sección <c>Mobile:GpsRetention</c>). El plazo es de cada oficina
/// (o el por defecto del dominio); aquí solo cada cuánto corre, cuánto procesa y el lock entre réplicas.
/// </summary>
public sealed class MileageGpsRetentionOptions
{
    public const string SectionName = "Mobile:GpsRetention";

    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 6;

    /// <summary>Viajes por lote: cada lote borra sus puntos y marca sus viajes en una transacción.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Tope de lotes por oficina en una pasada: una oficina con mucho atraso no acapara la pasada.</summary>
    public int MaxBatchesPerOffice { get; set; } = 20;

    /// <summary>Vida máxima del lock si la réplica que lo tiene muere a mitad de pasada.</summary>
    public int LockTtlMinutes { get; set; } = 60;

    public bool IsValid() =>
        IntervalHours is >= 1 and <= 168
        && BatchSize is >= 1 and <= 1000
        && MaxBatchesPerOffice is >= 1 and <= 1000
        && LockTtlMinutes is >= 1 and <= 1440;
}
