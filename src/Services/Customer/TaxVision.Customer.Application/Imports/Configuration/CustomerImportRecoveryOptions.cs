namespace TaxVision.Customer.Application.Imports.Configuration;

/// <summary>Barrido de imports atascados (sección "CustomerImport:Recovery"). Encendido por defecto.</summary>
public sealed class CustomerImportRecoveryOptions
{
    public const string SectionName = "CustomerImport:Recovery";

    public bool Enabled { get; set; } = true;

    /// <summary>Minutos sin avance para considerar atascado un import en curso.</summary>
    public int IdleMinutes { get; set; } = 15;

    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Reencolados antes de darlo por fallido.</summary>
    public int MaxRecoveries { get; set; } = 3;

    public int BatchSize { get; set; } = 50;
}
