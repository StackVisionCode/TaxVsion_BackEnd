namespace TaxVision.Calendar.Infrastructure.Jobs;

public sealed class CustomerDirectoryReconciliationOptions
{
    public const string SectionName = "Tasks:Reconciliation";

    /// <summary>Encendida por defecto: sólo completa un campo que falta, nunca borra ni pisa datos.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Tope de tenants por corrida, para que un gap grande no dispare un tick descontrolado.</summary>
    public int TenantLimitPerRun { get; set; } = 100;
}
