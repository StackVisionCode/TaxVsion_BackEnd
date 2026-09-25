namespace TaxVision.Billing.Application.Abstractions;

// Flag de visibilidad por asignación (default false = todos ven todas las facturas hasta sembrar la
// proyección con la reconciliación). Espejo de Customers:AssignmentVisibility. Encender por entorno.
public sealed class BillingVisibilityOptions
{
    public const string SectionName = "Billing:AssignmentVisibility";
    public bool Enabled { get; set; }
}
