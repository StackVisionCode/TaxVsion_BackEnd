namespace TaxVision.Notes.Infrastructure.Jobs;

/// <summary>Fase 4B — reconciliación periódica de DisplayName faltante en CustomerDirectoryEntries.</summary>
public sealed class CustomerDirectoryReconciliationOptions
{
    public const string SectionName = "Notes:Reconciliation";

    /// <summary>
    /// Habilitado por default: esta corrida nunca borra ni pierde datos, solo completa un campo
    /// (DisplayName) para que las filas coincidan con la fuente real (Customer) — mismo perfil de
    /// riesgo que el backfill reactivo de Fase 4B, que tampoco tiene flag de habilitación.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Tope de tenants con nombres faltantes procesados por corrida — evita un tick descontrolado si el gap crece.</summary>
    public int TenantLimitPerRun { get; set; } = 100;
}
