namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>Fase 16, plan §32 R1 — reconciliación periódica de <c>CustomerEmailAddresses</c> contra Customer.</summary>
public sealed class CustomerEmailReconciliationOptions
{
    public const string SectionName = "Correspondence:Reconciliation";

    /// <summary>
    /// Habilitado por default a diferencia de <see cref="DraftCleanupOptions.Enabled"/>: esta
    /// corrida nunca borra ni pierde datos, solo crea/actualiza/reactiva filas para que coincidan
    /// con la fuente real (Customer) — mismo perfil de riesgo que el backfill de Fase 2, que
    /// tampoco tiene flag de habilitación.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
