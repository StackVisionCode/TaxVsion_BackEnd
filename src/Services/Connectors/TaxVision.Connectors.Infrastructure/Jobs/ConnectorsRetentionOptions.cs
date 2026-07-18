namespace TaxVision.Connectors.Infrastructure.Jobs;

/// <summary>Fase 11 — retention de <c>ProviderConnectionAuditLog</c> (§26/§30 del plan: 90 días). Deshabilitado por default hasta que se autorice explícitamente.</summary>
public sealed class ConnectorsRetentionOptions
{
    public const string SectionName = "Connectors:Retention";

    public bool Enabled { get; set; }
    public int RetentionDays { get; set; } = 90;
    public int BatchSize { get; set; } = 500;
}
