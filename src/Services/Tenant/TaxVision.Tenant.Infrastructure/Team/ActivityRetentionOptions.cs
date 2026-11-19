namespace TaxVision.Tenant.Infrastructure.Team;

/// <summary>
/// Operación de la purga del historial (sección <c>Tenant:ActivityRetention</c>). Los 13 meses son del dominio;
/// aquí solo cada cuánto corre, el tamaño del lote y el lock entre réplicas.
/// </summary>
public sealed class ActivityRetentionOptions
{
    public const string SectionName = "Tenant:ActivityRetention";

    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 24;

    public int BatchSize { get; set; } = 1000;

    /// <summary>Vida máxima del lock si la réplica que lo tiene muere a mitad de pasada.</summary>
    public int LockTtlMinutes { get; set; } = 60;

    public bool IsValid() =>
        IntervalHours is >= 1 and <= 168 && BatchSize is >= 1 and <= 10_000 && LockTtlMinutes is >= 1 and <= 1440;
}
