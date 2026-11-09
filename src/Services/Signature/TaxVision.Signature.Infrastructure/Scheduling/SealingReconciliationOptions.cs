namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>Tiempos del job que vuelve a pedir el sellado de las solicitudes completadas sin sellar (SIG-2).</summary>
public sealed class SealingReconciliationOptions
{
    public const string SectionName = "Signature:SealingReconciliation";

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Antigüedad mínima de la completada: deja terminar el sellado normal y sus reintentos.</summary>
    public TimeSpan Grace { get; set; } = TimeSpan.FromMinutes(10);

    public int BatchSize { get; set; } = 50;

    /// <summary>Si se fija, solo se toman las completadas después de esta fecha; sin valor, también las históricas.</summary>
    public DateTime? CompletedAfterUtc { get; set; }
}
