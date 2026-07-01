namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Filtro de status para listados. Distinto de CustomerStatus del dominio
/// porque incluye "All" como concepto de query, no de estado.
/// </summary>
public enum CustomerStatusFilter
{
    Active, // default — Status == Active
    Inactive, // Status == Inactive
    Archived, // Status == Archived
    NotArchived, // Status != Archived (Active + Inactive) — comportamiento anterior
    All, // sin filtro
}
