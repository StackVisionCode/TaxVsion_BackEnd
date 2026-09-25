namespace BuildingBlocks.CustomerVisibility;

/// <summary>
/// Base URL directa a Customer.Api + toggle/intervalo del job que siembra la proyección de asignaciones.
/// El endpoint es interno (no pasa por el Gateway) → apunta directo al servicio. En Docker: http://customer-api:8080.
/// Cada servicio bindea su propia sección <c>CustomerVisibility:Reconciliation</c>.
/// </summary>
public sealed class CustomerVisibilityReconciliationOptions
{
    public const string SectionName = "CustomerVisibility:Reconciliation";

    public string CustomerBaseUrl { get; set; } = "http://localhost:5263";

    /// <summary>Si el job periódico corre. Default true (self-healing de la proyección).</summary>
    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>Cada cuántas horas re-pagina la fuente completa. Default 12h (backstop, no tiempo real).</summary>
    public int ReconciliationIntervalHours { get; set; } = 12;

    /// <summary>Tamaño de página al paginar el endpoint de reconciliación. Default 200.</summary>
    public int ReconciliationPageSize { get; set; } = 200;
}
